using Jotdex.Core.Configuration;
using Jotdex.Core.Snippets;
using Jotdex.Core.Vault;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Snippets;

public sealed class SnippetIndexService : ISnippetIndex, IVaultRescanObserver, IDisposable
{
    private readonly IDataRootResolver _dataRoot;
    private readonly IServiceProvider _services;
    private readonly ILogger<SnippetIndexService> _logger;
    private readonly object _gate = new();
    private SqliteConnection? _conn;
    private bool _schemaReady;

    public SnippetIndexService(IDataRootResolver dataRoot, IServiceProvider services, ILogger<SnippetIndexService> logger)
    {
        _dataRoot = dataRoot;
        _services = services;
        _logger = logger;
    }

    private IVaultService Vault => _services.GetRequiredService<IVaultService>();

    public void AfterVaultRescan() => RebuildFromVault();

    public void RebuildFromVault()
    {
        EnsureOpen();
        if (_conn is null) return;
        EnsureSchema();
        using var tx = _conn.BeginTransaction();
        using (var del = _conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM snippets_meta";
            del.ExecuteNonQuery();
        }

        var count = 0;
        foreach (var summary in Vault.ListNotes(null, includeSnippetNotes: true))
        {
            var detail = Vault.GetNote(summary.Id);
            if (detail is null) continue;
            var snippet = SnippetNoteParser.FromNote(detail);
            if (snippet is null) continue;
            InsertSnippet(tx, snippet);
            count++;
        }

        tx.Commit();
        _logger.LogInformation("Snippet index rebuilt with {Count} snippets", count);
    }

    public IReadOnlyList<SnippetSummary> List(string? query, string? language, int limit = 50)
    {
        EnsureOpen();
        if (_conn is null || !_schemaReady) return [];

        limit = Math.Clamp(limit, 1, 200);
        using var cmd = _conn.CreateCommand();
        var where = new List<string> { "1=1" };
        if (!string.IsNullOrWhiteSpace(language))
        {
            where.Add("language = $lang");
            cmd.Parameters.AddWithValue("$lang", language.Trim().ToLowerInvariant());
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            where.Add("(title LIKE $q OR trigger LIKE $q OR description LIKE $q OR code LIKE $q OR tags LIKE $q)");
            cmd.Parameters.AddWithValue("$q", "%" + query.Trim() + "%");
        }
        cmd.CommandText = $"""
            SELECT note_id, title, trigger, language, folder_path, relative_path, description, code, tags
            FROM snippets_meta
            WHERE {string.Join(" AND ", where)}
            ORDER BY title COLLATE NOCASE
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<SnippetSummary>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var tagsRaw = reader.IsDBNull(8) ? "" : reader.GetString(8);
            var tags = tagsRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            list.Add(new SnippetSummary(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                tags));
        }
        return list;
    }

    private void InsertSnippet(SqliteTransaction tx, SnippetSummary s)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO snippets_meta(note_id, title, trigger, language, folder_path, relative_path, description, code, tags)
            VALUES ($id, $title, $trigger, $lang, $folder, $rel, $desc, $code, $tags)
            """;
        cmd.Parameters.AddWithValue("$id", s.NoteId.ToString("D"));
        cmd.Parameters.AddWithValue("$title", s.Title);
        cmd.Parameters.AddWithValue("$trigger", s.Trigger);
        cmd.Parameters.AddWithValue("$lang", s.Language);
        cmd.Parameters.AddWithValue("$folder", s.FolderPath);
        cmd.Parameters.AddWithValue("$rel", s.RelativePath);
        cmd.Parameters.AddWithValue("$desc", (object?)s.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$code", s.Code);
        cmd.Parameters.AddWithValue("$tags", string.Join('\n', s.Tags));
        cmd.ExecuteNonQuery();
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
            EnsureSchema();
        }
    }

    private void EnsureSchema()
    {
        if (_schemaReady || _conn is null) return;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS snippets_meta (
              note_id TEXT PRIMARY KEY,
              title TEXT NOT NULL,
              trigger TEXT NOT NULL,
              language TEXT NOT NULL,
              folder_path TEXT NOT NULL,
              relative_path TEXT NOT NULL,
              description TEXT,
              code TEXT NOT NULL,
              tags TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_snippets_trigger ON snippets_meta(trigger);
            CREATE INDEX IF NOT EXISTS idx_snippets_language ON snippets_meta(language);
            """;
        cmd.ExecuteNonQuery();
        _schemaReady = true;
    }

    public void Dispose() => _conn?.Dispose();
}
