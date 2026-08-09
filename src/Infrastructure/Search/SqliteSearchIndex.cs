using System.Globalization;
using System.Text;
using Jotdex.Core.Configuration;
using Jotdex.Core.Search;
using Jotdex.Core.Vault;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Search;

public sealed class SqliteSearchIndex : ISearchIndex, IVaultRescanObserver, IDisposable
{
    private readonly IDataRootResolver _dataRoot;
    private readonly IServiceProvider _services;
    private readonly ILogger<SqliteSearchIndex> _logger;
    private readonly object _gate = new();
    private SqliteConnection? _conn;
    private bool _fts5;
    private bool _trigram;
    private string? _lastError;
    private bool _schemaReady;
    private bool _rebuilding;

    public SqliteSearchIndex(IDataRootResolver dataRoot, IServiceProvider services, ILogger<SqliteSearchIndex> logger)
    {
        _dataRoot = dataRoot;
        _services = services;
        _logger = logger;
    }

    private IVaultService Vault => _services.GetRequiredService<IVaultService>();

    public void AfterVaultRescan() => RebuildFromVault();

    public IndexStatus Probe()
    {
        EnsureOpen();
        var count = 0;
        if (_schemaReady && _conn is not null)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM notes_meta";
            count = Convert.ToInt32(cmd.ExecuteScalar());
        }

        return new IndexStatus
        {
            Ready = _schemaReady && _fts5,
            Fts5 = _fts5,
            Trigram = _trigram,
            NoteCount = count,
            LastError = _lastError
        };
    }

    public void RebuildFromVault()
    {
        lock (_gate)
        {
            if (_rebuilding) return;
            _rebuilding = true;
        }

        try
        {
            EnsureOpen();
            if (_conn is null || !_fts5)
            {
                _lastError = "FTS5 unavailable";
                return;
            }

            EnsureSchema();
            using var tx = _conn.BeginTransaction();
            Exec(tx, "DELETE FROM notes_meta");
            Exec(tx, "DELETE FROM notes_fts");
            if (_trigram) Exec(tx, "DELETE FROM notes_tri");

            foreach (var summary in Vault.ListNotes(null))
            {
                var detail = Vault.GetNote(summary.Id);
                if (detail is null) continue;
                var body = Jotdex.Infrastructure.Vault.FrontMatterParser.Parse(detail.Markdown).Body;
                var attText = AttachmentTextExtractor.Extract(Vault, detail.Attachments);
                var doc = NoteTextExtractor.FromIndexed(
                    detail.Id,
                    detail.Title,
                    detail.RelativePath,
                    detail.FolderPath,
                    detail.Tags,
                    body,
                    detail.Attachments.Select(a => a.FileName),
                    detail.Modified,
                    detail.Attachments.Count > 0,
                    attText);

                InsertDocument(tx, doc);
            }

            tx.Commit();
            _lastError = null;
            _logger.LogInformation("Search index rebuilt with {Count} notes", Probe().NoteCount);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning(ex, "Search index rebuild failed");
        }
        finally
        {
            lock (_gate) _rebuilding = false;
        }
    }

    public SearchResponse Search(SearchRequest request)
    {
        EnsureOpen();
        if (_conn is null || !_schemaReady || !_fts5)
        {
            return new SearchResponse
            {
                Mode = "unavailable",
                ParsedQuery = request.RawQuery,
                Hits = [],
                Warning = _lastError ?? "Search index not ready"
            };
        }

        var parsed = SearchQueryParser.Parse(request.RawQuery, request.ForceLiteral);
        // Literal/trigram for explicit quotes or force flag; allow 2+ chars (e.g. "IP")
        var useLiteral = parsed.Literal && _trigram && parsed.FreeText.Length >= 2;
        if (parsed.Literal && parsed.FreeText.Length > 0 && parsed.FreeText.Length < 2)
        {
            return new SearchResponse
            {
                Mode = "literal",
                ParsedQuery = parsed.Display,
                Hits = [],
                Warning = "Literal search needs at least 2 characters"
            };
        }

        List<SearchHit> hits;
        string mode;
        if (useLiteral)
        {
            hits = SearchLiteral(parsed, request.Limit);
            mode = "literal";
        }
        else if (string.IsNullOrWhiteSpace(parsed.FreeText) && string.IsNullOrWhiteSpace(parsed.Title))
        {
            hits = SearchFiltersOnly(parsed, request.Limit);
            mode = "smart";
        }
        else
        {
            // Hybrid: prefix FTS + substring fallback so partials always work
            hits = SearchHybrid(parsed, request.Limit);
            mode = "smart";
        }

        return new SearchResponse
        {
            Mode = mode,
            ParsedQuery = parsed.Display,
            Hits = hits
        };
    }

    private List<SearchHit> SearchHybrid(ParsedSearchQuery parsed, int limit)
    {
        var byId = new Dictionary<Guid, SearchHit>();

        void Merge(IEnumerable<SearchHit> batch, double boost = 0)
        {
            foreach (var hit in batch)
            {
                if (byId.TryGetValue(hit.NoteId, out var existing))
                {
                    if (hit.Score + boost < existing.Score) // bm25 lower is better
                        byId[hit.NoteId] = hit with { Score = hit.Score + boost, Snippet = hit.Snippet ?? existing.Snippet };
                    else if (existing.Snippet is null && hit.Snippet is not null)
                        byId[hit.NoteId] = existing with { Snippet = hit.Snippet };
                }
                else
                {
                    byId[hit.NoteId] = hit with { Score = hit.Score + boost };
                }
            }
        }

        try { Merge(SearchSmart(parsed, limit)); }
        catch { /* FTS query edge cases */ }

        var tokens = string.IsNullOrWhiteSpace(parsed.FreeText)
            ? []
            : parsed.FreeText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Trigram substring (needs ≥3 chars): aqua mid-token, etc.
        if (_trigram && tokens.Length == 1 && tokens[0].Length >= 3)
        {
            try
            {
                Merge(SearchLiteral(parsed with { FreeText = tokens[0], Literal = true }, limit), boost: 0.5);
            }
            catch { }
        }
        else if (_trigram && tokens.Length > 1 && tokens.Any(t => t.Length >= 3))
        {
            try { Merge(SearchSubstringAnd(parsed, tokens, limit), boost: 1.0); }
            catch { }
        }

        // LIKE on title/body/tags always fills gaps (short tokens like "ip", stubborn partials)
        if (tokens.Length > 0 && (byId.Count == 0 || tokens.Any(t => t.Length < 3)))
        {
            try { Merge(SearchLikeFallback(parsed, limit), boost: 1.5); }
            catch { }
        }

        return byId.Values
            .OrderBy(h => h.Score)
            .ThenBy(h => h.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private List<SearchHit> SearchSubstringAnd(ParsedSearchQuery parsed, string[] tokens, int limit)
    {
        if (!_trigram || _conn is null) return [];
        // Start from notes matching first token via trigram, filter in memory for remaining
        var first = SearchLiteral(parsed with { FreeText = tokens[0], Literal = true }, Math.Max(limit * 5, 50));
        var rest = tokens.Skip(1).Select(t => t.ToLowerInvariant()).ToArray();
        if (rest.Length == 0) return first.Take(limit).ToList();

        var filtered = new List<SearchHit>();
        foreach (var hit in first)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT f.title || ' ' || f.folder_path || ' ' || f.tags || ' ' || f.headings || ' ' || f.body || ' ' || f.code || ' ' || f.attachment_names
                FROM notes_fts f WHERE f.note_id = @id LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@id", hit.NoteId.ToString("D"));
            var blob = cmd.ExecuteScalar()?.ToString()?.ToLowerInvariant() ?? "";
            if (rest.All(t => blob.Contains(t, StringComparison.Ordinal)))
                filtered.Add(hit);
            if (filtered.Count >= limit) break;
        }

        return filtered;
    }

    private List<SearchHit> SearchLikeFallback(ParsedSearchQuery parsed, int limit)
    {
        var tokens = parsed.FreeText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return [];

        var sql = new StringBuilder("""
            SELECT m.id, m.title, m.folder_path, m.relative_path, m.tags, m.modified, m.has_attachments,
                   substr(f.body, 1, 120) AS snip,
                   5.0 AS score
            FROM notes_meta m
            JOIN notes_fts f ON f.note_id = m.id
            WHERE 1=1
            """);
        for (var i = 0; i < tokens.Length; i++)
        {
            sql.Append($" AND (lower(f.title) LIKE @t{i} OR lower(f.body) LIKE @t{i} OR lower(f.headings) LIKE @t{i} OR lower(f.code) LIKE @t{i} OR lower(f.tags) LIKE @t{i} OR lower(f.folder_path) LIKE @t{i} OR lower(f.attachment_names) LIKE @t{i})");
        }

        AppendFilters(sql, parsed, applyInCodeOnFts: false, out var codeHint);
        sql.Append(" ORDER BY m.title LIMIT @limit");

        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql.ToString();
        for (var i = 0; i < tokens.Length; i++)
            cmd.Parameters.AddWithValue($"@t{i}", "%" + tokens[i].ToLowerInvariant() + "%");
        cmd.Parameters.AddWithValue("@limit", limit);
        AddFilterParams(cmd, parsed);
        return ReadHits(cmd, codeHint);
    }

    private List<SearchHit> SearchSmart(ParsedSearchQuery parsed, int limit)
    {
        var sql = new StringBuilder("""
            SELECT m.id, m.title, m.folder_path, m.relative_path, m.tags, m.modified, m.has_attachments,
                   snippet(notes_fts, 4, '«', '»', '…', 24) AS snip,
                   bm25(notes_fts, 10.0, 2.0, 6.0, 4.0, 1.0, 3.0, 2.0) AS score
            FROM notes_fts
            JOIN notes_meta m ON m.id = notes_fts.note_id
            WHERE notes_fts MATCH @q
            """);

        AppendFilters(sql, parsed, applyInCodeOnFts: true, out var hasCodeMatchHint);
        sql.Append(" ORDER BY score LIMIT @limit");

        var ftsQuery = BuildFtsMatch(parsed);
        if (string.IsNullOrWhiteSpace(ftsQuery))
            return [];

        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("@q", ftsQuery);
        cmd.Parameters.AddWithValue("@limit", limit);
        AddFilterParams(cmd, parsed);

        return ReadHits(cmd, hasCodeMatchHint);
    }

    private List<SearchHit> SearchFiltersOnly(ParsedSearchQuery parsed, int limit)
    {
        var sql = new StringBuilder("""
            SELECT m.id, m.title, m.folder_path, m.relative_path, m.tags, m.modified, m.has_attachments,
                   NULL AS snip,
                   0 AS score
            FROM notes_meta m
            WHERE 1=1
            """);
        AppendFilters(sql, parsed, applyInCodeOnFts: false, out var codeHint);
        if (parsed.InCode)
            sql.Append(" AND EXISTS (SELECT 1 FROM notes_fts f WHERE f.note_id = m.id AND f.code != '')");
        sql.Append(" ORDER BY m.title LIMIT @limit");

        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("@limit", limit);
        AddFilterParams(cmd, parsed);
        return ReadHits(cmd, codeHint);
    }

    private List<SearchHit> SearchLiteral(ParsedSearchQuery parsed, int limit)
    {
        var sql = new StringBuilder("""
            SELECT m.id, m.title, m.folder_path, m.relative_path, m.tags, m.modified, m.has_attachments,
                   snippet(notes_tri, 1, '«', '»', '…', 24) AS snip,
                   bm25(notes_tri) AS score
            FROM notes_tri
            JOIN notes_meta m ON m.id = notes_tri.note_id
            WHERE notes_tri MATCH @q
            """);
        AppendFilters(sql, parsed, applyInCodeOnFts: false, out _);
        sql.Append(" ORDER BY score LIMIT @limit");

        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql.ToString();
        // trigram MATCH uses the raw substring as the query token
        cmd.Parameters.AddWithValue("@q", EscapeFts(parsed.FreeText));
        cmd.Parameters.AddWithValue("@limit", limit);
        AddFilterParams(cmd, parsed);
        return ReadHits(cmd, codeHint: parsed.InCode);
    }

    private static void AppendFilters(StringBuilder sql, ParsedSearchQuery parsed, bool applyInCodeOnFts, out bool codeHint)
    {
        codeHint = parsed.InCode;
        if (!string.IsNullOrWhiteSpace(parsed.Folder))
            sql.Append("""
                 AND (
                   m.folder_path = @folder
                   OR m.folder_path LIKE @folderPrefix
                   OR m.folder_path LIKE @folderSuffix
                   OR m.folder_path LIKE @folderMid
                 )
                """);
        if (!string.IsNullOrWhiteSpace(parsed.Tag))
            sql.Append(" AND lower(m.tags) LIKE @tag");
        if (!string.IsNullOrWhiteSpace(parsed.Title))
            sql.Append(" AND lower(m.title) LIKE @title");
        if (parsed.HasAttachment)
            sql.Append(" AND m.has_attachments = 1");
        if (parsed.ModifiedWithinDays is int)
            sql.Append(" AND m.modified >= @modifiedAfter");
        if (applyInCodeOnFts && parsed.InCode)
            sql.Append(" AND notes_fts.code != ''");
    }

    private static void AddFilterParams(SqliteCommand cmd, ParsedSearchQuery parsed)
    {
        if (!string.IsNullOrWhiteSpace(parsed.Folder))
        {
            var folder = parsed.Folder.Replace('\\', '/').Trim('/');
            cmd.Parameters.AddWithValue("@folder", folder);
            cmd.Parameters.AddWithValue("@folderPrefix", folder + "/%");
            cmd.Parameters.AddWithValue("@folderSuffix", "%/" + folder);
            cmd.Parameters.AddWithValue("@folderMid", "%/" + folder + "/%");
        }

        if (!string.IsNullOrWhiteSpace(parsed.Tag))
            cmd.Parameters.AddWithValue("@tag", "%" + parsed.Tag.ToLowerInvariant() + "%");
        if (!string.IsNullOrWhiteSpace(parsed.Title))
            cmd.Parameters.AddWithValue("@title", "%" + parsed.Title.ToLowerInvariant() + "%");
        if (parsed.ModifiedWithinDays is int days)
            cmd.Parameters.AddWithValue("@modifiedAfter",
                DateTimeOffset.UtcNow.AddDays(-days).ToString("O", CultureInfo.InvariantCulture));
    }

    private List<SearchHit> ReadHits(SqliteCommand cmd, bool codeHint)
    {
        var list = new List<SearchHit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = Guid.Parse(reader.GetString(0));
            var tagsRaw = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var tags = tagsRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            DateTimeOffset? modified = null;
            if (!reader.IsDBNull(5) && DateTimeOffset.TryParse(reader.GetString(5), out var dt))
                modified = dt;

            list.Add(new SearchHit
            {
                NoteId = id,
                Title = reader.GetString(1),
                FolderPath = reader.IsDBNull(2) ? "" : reader.GetString(2),
                RelativePath = reader.GetString(3),
                Tags = tags,
                Modified = modified,
                Snippet = reader.IsDBNull(7) ? null : reader.GetString(7),
                Score = reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                HasCodeMatch = codeHint,
                HasAttachmentMatch = !reader.IsDBNull(6) && reader.GetInt32(6) == 1
            });
        }

        return list;
    }

    private static string BuildFtsMatch(ParsedSearchQuery parsed)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(parsed.Title))
        {
            var titleToken = PrefixToken(parsed.Title);
            parts.Add($"title:{titleToken}");
        }

        if (!string.IsNullOrWhiteSpace(parsed.FreeText))
        {
            var tokens = parsed.FreeText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                var t = PrefixToken(token);
                if (parsed.InCode)
                    parts.Add($"code:{t}");
                else
                    // Prefix match across all indexed fields so "aqua" hits "aquarium", "ip" hits "ipsec"
                    parts.Add($"(title:{t} OR tags:{t} OR headings:{t} OR body:{t} OR code:{t} OR attachment_names:{t} OR folder_path:{t})");
            }
        }

        return string.Join(" AND ", parts);
    }

    /// <summary>FTS5 prefix token: aqua → aqua* (quoted when needed).</summary>
    private static string PrefixToken(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return "\"\"";
        // Strip trailing * from user input then add our own
        value = value.TrimEnd('*');
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        if (escaped.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-'))
            return $"\"{escaped}\"*";
        return escaped + "*";
    }

    private static string EscapeFts(string value)
    {
        value = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        if (value.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-'))
            return $"\"{value}\"";
        return value;
    }

    private void InsertDocument(SqliteTransaction tx, NoteSearchDocument doc)
    {
        using (var meta = _conn!.CreateCommand())
        {
            meta.Transaction = tx;
            meta.CommandText = """
                INSERT INTO notes_meta(id, title, relative_path, folder_path, tags, modified, has_attachments)
                VALUES ($id, $title, $rel, $folder, $tags, $modified, $has)
                """;
            meta.Parameters.AddWithValue("$id", doc.NoteId.ToString("D"));
            meta.Parameters.AddWithValue("$title", doc.Title);
            meta.Parameters.AddWithValue("$rel", doc.RelativePath);
            meta.Parameters.AddWithValue("$folder", doc.FolderPath);
            meta.Parameters.AddWithValue("$tags", doc.Tags);
            meta.Parameters.AddWithValue("$modified", doc.Modified?.ToString("O") ?? (object)DBNull.Value);
            meta.Parameters.AddWithValue("$has", doc.HasAttachments ? 1 : 0);
            meta.ExecuteNonQuery();
        }

        using (var fts = _conn.CreateCommand())
        {
            fts.Transaction = tx;
            fts.CommandText = """
                INSERT INTO notes_fts(note_id, title, folder_path, tags, headings, body, code, attachment_names)
                VALUES ($id, $title, $folder, $tags, $headings, $body, $code, $att)
                """;
            fts.Parameters.AddWithValue("$id", doc.NoteId.ToString("D"));
            fts.Parameters.AddWithValue("$title", doc.Title);
            fts.Parameters.AddWithValue("$folder", doc.FolderPath);
            fts.Parameters.AddWithValue("$tags", doc.Tags);
            fts.Parameters.AddWithValue("$headings", doc.Headings);
            fts.Parameters.AddWithValue("$body", doc.Body);
            fts.Parameters.AddWithValue("$code", doc.Code);
            fts.Parameters.AddWithValue("$att", doc.AttachmentNames);
            fts.ExecuteNonQuery();
        }

        if (_trigram)
        {
            using var tri = _conn.CreateCommand();
            tri.Transaction = tx;
            tri.CommandText = "INSERT INTO notes_tri(note_id, content) VALUES ($id, $content)";
            tri.Parameters.AddWithValue("$id", doc.NoteId.ToString("D"));
            tri.Parameters.AddWithValue("$content", doc.CombinedLiteral);
            tri.ExecuteNonQuery();
        }
    }

    private void EnsureOpen()
    {
        if (_conn is not null) return;
        lock (_gate)
        {
            if (_conn is not null) return;
            SQLitePCL.Batteries_V2.Init();
            var dir = Path.Combine(_dataRoot.ResolveDataRoot(), "indexes");
            Directory.CreateDirectory(dir);
            var dbPath = Path.Combine(dir, "search.db");
            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            _conn = conn;
            ProbeCapabilities(conn);
            if (_fts5) EnsureSchema();
        }
    }

    private void ProbeCapabilities(SqliteConnection conn)
    {
        try
        {
            using (var ver = conn.CreateCommand())
            {
                ver.CommandText = "SELECT sqlite_version();";
                _logger.LogInformation("SQLite version {Version}", ver.ExecuteScalar());
            }

            using (var fts = conn.CreateCommand())
            {
                fts.CommandText = "SELECT sqlite_compileoption_used('ENABLE_FTS5');";
                _fts5 = Convert.ToInt32(fts.ExecuteScalar()) == 1;
            }

            if (_fts5)
            {
                try
                {
                    using var probe = conn.CreateCommand();
                    probe.CommandText = "CREATE VIRTUAL TABLE temp.fts_probe USING fts5(body, tokenize='trigram'); DROP TABLE temp.fts_probe;";
                    probe.ExecuteNonQuery();
                    _trigram = true;
                }
                catch (Exception ex)
                {
                    _trigram = false;
                    _logger.LogWarning(ex, "SQLite trigram tokenizer unavailable; literal search limited");
                }
            }
            else
            {
                _lastError = "FTS5 not enabled in SQLite build";
                _logger.LogError("FTS5 not available");
            }
        }
        catch (Exception ex)
        {
            _fts5 = false;
            _trigram = false;
            _lastError = ex.Message;
            _logger.LogError(ex, "SQLite capability probe failed");
        }
    }

    private void EnsureSchema()
    {
        if (_schemaReady || _conn is null) return;
        Exec(null, """
            CREATE TABLE IF NOT EXISTS notes_meta (
              id TEXT PRIMARY KEY,
              title TEXT NOT NULL,
              relative_path TEXT NOT NULL,
              folder_path TEXT NOT NULL,
              tags TEXT NOT NULL,
              modified TEXT,
              has_attachments INTEGER NOT NULL DEFAULT 0
            );
            """);
        Exec(null, """
            CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
              note_id UNINDEXED,
              title,
              folder_path,
              tags,
              headings,
              body,
              code,
              attachment_names,
              tokenize = 'unicode61'
            );
            """);
        if (_trigram)
        {
            Exec(null, """
                CREATE VIRTUAL TABLE IF NOT EXISTS notes_tri USING fts5(
                  note_id UNINDEXED,
                  content,
                  tokenize = 'trigram'
                );
                """);
        }

        _schemaReady = true;
    }

    private void Exec(SqliteTransaction? tx, string sql)
    {
        using var cmd = _conn!.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn?.Dispose();
}
