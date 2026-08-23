using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jotdex.Tests.Smoke;

public class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = TestHost.CreateClient(factory);
    }

    [Fact]
    public async Task Health_returns_ok_json()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", json);
        Assert.Contains("vaultConfigured", json);
    }

    [Fact]
    public async Task Spa_index_is_served()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Jotdex", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sample_vault_tree_and_notes_load()
    {
        var vault = await _client.GetFromJsonAsync<VaultDto>("/api/vault");
        Assert.NotNull(vault);
        Assert.True(vault!.Configured);
        Assert.True(vault.NoteCount >= 1);

        var tree = await _client.GetAsync("/api/tree");
        Assert.Equal(HttpStatusCode.OK, tree.StatusCode);

        var notes = await _client.GetFromJsonAsync<List<NoteDto>>("/api/notes");
        Assert.NotNull(notes);
        Assert.NotEmpty(notes!);

        var detail = await _client.GetAsync($"/api/notes/{notes[0].Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadAsStringAsync();
        Assert.Contains("html", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snippets_save_under_Snippets_folder_and_stay_out_of_notes_list()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var title = "Smoke snippet " + suffix;
        var trigger = "smoke-snip-" + suffix;
        var create = await _client.PostAsJsonAsync("/api/snippets", new
        {
            title,
            trigger,
            language = "powershell",
            code = "Get-Date",
            description = "Smoke test snippet",
            tags = new[] { "smoke" }
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<SnippetCreateDto>();
        Assert.NotNull(created?.Snippet);
        Assert.Equal("Snippets", created!.Snippet!.FolderPath, ignoreCase: true);
        Assert.Equal(trigger, created!.Snippet!.Trigger);

        var list = await _client.GetFromJsonAsync<SnippetListDto>($"/api/snippets?q={trigger}");
        Assert.NotNull(list?.Items);
        Assert.Contains(list!.Items!, s => s.Title == title);

        var notes = await _client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes");
        Assert.NotNull(notes);
        Assert.DoesNotContain(notes!, n => n.Title == title);

        var treeJson = await _client.GetStringAsync("/api/tree");
        Assert.DoesNotContain("\"name\":\"Snippets\"", treeJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snippets_reject_duplicate_shortcut()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var trigger = $"dup-{suffix}";
        var first = await _client.PostAsJsonAsync("/api/snippets", new
        {
            title = $"First {suffix}",
            trigger,
            language = "powershell",
            code = "Get-Date",
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/snippets", new
        {
            title = $"Second {suffix}",
            trigger,
            language = "powershell",
            code = "Get-Process",
        });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var err = await second.Content.ReadAsStringAsync();
        Assert.Contains("already exists", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_finds_technical_and_prose_terms()
    {
        await _client.PostAsync("/api/admin/reindex", null);

        var status = await _client.GetFromJsonAsync<IndexDto>("/api/admin/reindex/status");
        Assert.NotNull(status);
        Assert.True(status!.Ready);
        Assert.True(status.Fts5);
        Assert.True(status.NoteCount >= 1);

        var vpn = await _client.GetFromJsonAsync<SearchDto>("/api/search?q=ipsec");
        Assert.NotNull(vpn);
        Assert.NotEmpty(vpn!.Hits);

        var prefix = await _client.GetFromJsonAsync<SearchDto>("/api/search?q=aqua");
        Assert.NotNull(prefix);
        Assert.Contains(prefix!.Hits, h => h.Title.Contains("Aquarium", StringComparison.OrdinalIgnoreCase)
            || h.Title.Contains("Nitrogen", StringComparison.OrdinalIgnoreCase));

        var partial = await _client.GetFromJsonAsync<SearchDto>("/api/search?q=ip");
        Assert.NotNull(partial);
        Assert.Contains(partial!.Hits, h => h.Title.Contains("IPsec", StringComparison.OrdinalIgnoreCase));

        var multi = await _client.GetFromJsonAsync<SearchDto>("/api/search?q=opnsense%20vpn");
        Assert.NotNull(multi);
        Assert.NotEmpty(multi!.Hits);

        var literal = await _client.GetFromJsonAsync<SearchDto>("/api/search?q=%220x80070005%22");
        Assert.NotNull(literal);
        Assert.Equal("literal", literal!.Mode);
        Assert.NotEmpty(literal.Hits);

        var folder = await _client.GetFromJsonAsync<SearchDto>("/api/search?q=folder%3ANetworking");
        Assert.NotNull(folder);
        Assert.NotEmpty(folder!.Hits);
    }

    [Fact]
    public async Task Folder_create_rename_delete_round_trip()
    {
        var name = "TmpFolder-" + Guid.NewGuid().ToString("N")[..8];
        var create = await _client.PostAsJsonAsync("/api/folders", new { name, parent = "" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<FolderDto>();
        Assert.NotNull(created);
        Assert.True(created!.Success);
        Assert.Equal(name, created.Path);

        var renamedName = name + "-renamed";
        var patch = await _client.PatchAsJsonAsync("/api/folders", new { path = created.Path, newName = renamedName });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var renamed = await patch.Content.ReadFromJsonAsync<FolderDto>();
        Assert.NotNull(renamed);
        Assert.Equal(renamedName, renamed!.Path);

        var del = await _client.DeleteAsync($"/api/folders?path={Uri.EscapeDataString(renamed.Path!)}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
    }

    [Fact]
    public async Task Note_rename_rewrites_asset_links_and_moves_assets()
    {
        var title = "LinkRewrite " + Guid.NewGuid().ToString("N")[..6];
        var create = await _client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "",
            markdown = $"# {title}\n\n![x]({Uri.EscapeDataString(title)}.assets/pic.png)\n"
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        var settings = await _client.GetFromJsonAsync<VaultSettingsDto>("/api/settings/vault");
        Assert.NotNull(settings?.VaultPath);
        var assetsDir = Path.Combine(settings!.VaultPath!, title + ".assets");
        Directory.CreateDirectory(assetsDir);
        await File.WriteAllBytesAsync(Path.Combine(assetsDir, "pic.png"), [1, 2, 3, 4]);

        var newTitle = title + " Renamed";
        var move = await _client.PostAsJsonAsync($"/api/notes/{note!.Id}/move", new { folder = "", title = newTitle });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var moved = await move.Content.ReadFromJsonAsync<MoveDto>();
        Assert.NotNull(moved);
        Assert.True(moved!.Success);
        Assert.NotNull(moved.Note);
        Assert.Contains(Uri.EscapeDataString(newTitle) + ".assets/pic.png", moved.Note!.Markdown);
        Assert.True(Directory.Exists(Path.Combine(settings.VaultPath!, newTitle + ".assets")));
        Assert.False(Directory.Exists(Path.Combine(settings.VaultPath!, title + ".assets")));

        var trash = await _client.DeleteAsync($"/api/notes/{note.Id}");
        Assert.Equal(HttpStatusCode.NoContent, trash.StatusCode);
    }

    [Fact]
    public async Task Save_conflict_returns_409_and_force_overwrites()
    {
        var create = await _client.PostAsJsonAsync("/api/notes", new
        {
            title = "Conflict " + Guid.NewGuid().ToString("N")[..6],
            folder = "",
            markdown = "# A\n\n"
        });
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);
        try
        {
            var bad = await _client.PutAsJsonAsync($"/api/notes/{note!.Id}", new
            {
                markdown = "# B\n\n",
                etag = "deadbeef"
            });
            Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);

            var force = await _client.PutAsJsonAsync($"/api/notes/{note.Id}", new
            {
                markdown = "# ConflictOverwriteOk\n\n",
                etag = "deadbeef",
                force = true
            });
            Assert.Equal(HttpStatusCode.OK, force.StatusCode);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Attachment_upload_writes_assets_and_returns_markdown_path()
    {
        var create = await _client.PostAsJsonAsync("/api/notes", new
        {
            title = "Upload " + Guid.NewGuid().ToString("N")[..6],
            folder = "",
            markdown = "# U\n\n"
        });
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);
        try
        {
            using var content = new MultipartFormDataContent();
            var bytes = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10]);
            bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(bytes, "file", "shot.png");

            var upload = await _client.PostAsync($"/api/notes/{note!.Id}/attachments", content);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            var result = await upload.Content.ReadFromJsonAsync<UploadDto>();
            Assert.NotNull(result);
            Assert.True(result!.Success);
            Assert.Contains(".assets/", result.MarkdownPath);
            Assert.True(result.IsImage);
            Assert.False(string.IsNullOrWhiteSpace(result.AttachmentId));

            var settings = await _client.GetFromJsonAsync<VaultSettingsDto>("/api/settings/vault");
            Assert.NotNull(settings?.VaultPath);
            Assert.Contains(
                Directory.GetFiles(settings!.VaultPath!, "*", SearchOption.AllDirectories),
                f => f.EndsWith(result.FileName!, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Save_preserves_front_matter_code_and_relative_image_link()
    {
        var body = """
            # Round trip

            ```powershell
            Get-ChildItem C:\Temp
            ```

            - [ ] task one
            - [x] task two

            | A | B |
            | - | - |
            | 1 | 2 |

            ![shot](Round%20Trip.assets/pic.png)

            [[Unresolved Wiki]]
            """;

        var create = await _client.PostAsJsonAsync("/api/notes", new
        {
            title = "Round Trip " + Guid.NewGuid().ToString("N")[..6],
            folder = "",
            markdown = body
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        try
        {
            var get = await _client.GetFromJsonAsync<NoteDetailDto>($"/api/notes/{note!.Id}");
            Assert.NotNull(get);
            Assert.Contains("id:", get!.Markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("```powershell", get.Markdown, StringComparison.Ordinal);
            Assert.Contains("Get-ChildItem C:\\Temp", get.Markdown, StringComparison.Ordinal);
            Assert.Contains("- [ ] task one", get.Markdown, StringComparison.Ordinal);
            Assert.Contains("Round%20Trip.assets/pic.png", get.Markdown, StringComparison.Ordinal);
            Assert.Contains("[[Unresolved Wiki]]", get.Markdown, StringComparison.Ordinal);

            var save = await _client.PutAsJsonAsync($"/api/notes/{note.Id}", new
            {
                markdown = get.Markdown,
                etag = get.ETag
            });
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var saved = await save.Content.ReadFromJsonAsync<SaveResultDto>();
            Assert.NotNull(saved);
            Assert.True(saved!.Success);
            Assert.Contains("```powershell", saved.Note!.Markdown, StringComparison.Ordinal);
            Assert.Contains("[[Unresolved Wiki]]", saved.Note.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Localize_images_rejects_loopback_url()
    {
        var create = await _client.PostAsJsonAsync("/api/notes", new
        {
            title = "Localize " + Guid.NewGuid().ToString("N")[..6],
            folder = "",
            markdown = "# L\n\n![x](http://127.0.0.1/secret.png)\n"
        });
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);
        try
        {
            var res = await _client.PostAsJsonAsync($"/api/notes/{note!.Id}/localize-images", new
            {
                urls = new[] { "http://127.0.0.1/secret.png" },
                etag = note.ETag
            });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            var json = await res.Content.ReadAsStringAsync();
            Assert.Contains("blocked", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Preserve_page_writes_sanitized_sidecar_and_embed()
    {
        var title = "Preserve " + Guid.NewGuid().ToString("N")[..6];
        var create = await _client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "",
            markdown = "# Clip me\n\nBody.\n"
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        try
        {
            var html = """
                <div onclick="alert(1)">
                  <h1>Vendor docs</h1>
                  <script>document.cookie</script>
                  <p>Keep this paragraph.</p>
                  <a href="javascript:evil()">x</a>
                </div>
                """;

            var res = await _client.PostAsJsonAsync($"/api/notes/{note!.Id}/preserve-page", new
            {
                html,
                sourceUrl = "https://example.com/docs",
                etag = note.ETag
            });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            Assert.Contains("\"success\":true", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("clipped-page-", body, StringComparison.OrdinalIgnoreCase);

            var updated = await _client.GetFromJsonAsync<NoteDetailDto>($"/api/notes/{note.Id}");
            Assert.NotNull(updated);
            Assert.Contains("Clipped page", updated!.Markdown, StringComparison.Ordinal);
            Assert.Contains(".assets/", updated.Markdown, StringComparison.Ordinal);
            Assert.Contains("Open clipped page", updated.Markdown, StringComparison.Ordinal);

            var settings = await _client.GetFromJsonAsync<VaultSettingsDto>("/api/settings/vault");
            Assert.NotNull(settings?.VaultPath);
            var assetsDir = Path.Combine(settings!.VaultPath!, title + ".assets");
            Assert.True(Directory.Exists(assetsDir));
            var sidecar = Directory.GetFiles(assetsDir, "*clipped-page*.html").SingleOrDefault();
            Assert.NotNull(sidecar);
            var sidecarHtml = await File.ReadAllTextAsync(sidecar!);
            Assert.DoesNotContain("<script", sidecarHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick", sidecarHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Keep this paragraph", sidecarHtml, StringComparison.Ordinal);
            Assert.Contains("https://example.com/docs", sidecarHtml, StringComparison.Ordinal);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Open_close_without_edits_does_not_rewrite_file()
    {
        var title = "NoRewrite " + Guid.NewGuid().ToString("N")[..6];
        var create = await _client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "",
            markdown = "# Stable\n\nDo not touch.\n"
        });
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        try
        {
            var settings = await _client.GetFromJsonAsync<VaultSettingsDto>("/api/settings/vault");
            Assert.NotNull(settings?.VaultPath);
            var path = Path.Combine(settings!.VaultPath!, title + ".md");
            Assert.True(File.Exists(path));
            var beforeWrite = File.GetLastWriteTimeUtc(path);
            var beforeBytes = await File.ReadAllBytesAsync(path);
            await Task.Delay(50);

            var save = await _client.PutAsJsonAsync($"/api/notes/{note!.Id}", new
            {
                markdown = note.Markdown,
                etag = note.ETag
            });
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var result = await save.Content.ReadFromJsonAsync<SaveResultDto>();
            Assert.True(result!.Success);

            var afterWrite = File.GetLastWriteTimeUtc(path);
            var afterBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(beforeWrite, afterWrite);
            Assert.Equal(beforeBytes, afterBytes);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Html_sidecar_reference_preserved_on_save()
    {
        var title = "Sidecar " + Guid.NewGuid().ToString("N")[..6];
        var body =
            "# Vendor Clip\n\n<details>\n<summary>Preserved page snapshot</summary>\n\n" +
            $"[Open clipped page]({Uri.EscapeDataString(title)}.assets/clipped-page.html)\n\n</details>\n";

        var create = await _client.PostAsJsonAsync("/api/notes", new { title, folder = "", markdown = body });
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        try
        {
            var settings = await _client.GetFromJsonAsync<VaultSettingsDto>("/api/settings/vault");
            Assert.NotNull(settings?.VaultPath);
            var assetsDir = Path.Combine(settings!.VaultPath!, title + ".assets");
            Directory.CreateDirectory(assetsDir);
            await File.WriteAllTextAsync(
                Path.Combine(assetsDir, "clipped-page.html"),
                "<!DOCTYPE html><html><body><h1>ok</h1></body></html>");

            var get = await _client.GetFromJsonAsync<NoteDetailDto>($"/api/notes/{note!.Id}");
            Assert.NotNull(get);
            Assert.Contains("clipped-page.html", get!.Markdown, StringComparison.Ordinal);

            var save = await _client.PutAsJsonAsync($"/api/notes/{note.Id}", new
            {
                markdown = get.Markdown,
                etag = get.ETag
            });
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var saved = await save.Content.ReadFromJsonAsync<SaveResultDto>();
            Assert.True(saved!.Success);
            Assert.Contains("clipped-page.html", saved.Note!.Markdown, StringComparison.Ordinal);
            Assert.Contains("</details>", saved.Note.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Csp_header_is_present_on_spa()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        var csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Raw_html_blocks_preserved_on_content_save()
    {
        var title = "RawHtml " + Guid.NewGuid().ToString("N")[..6];
        var body = """
            # Raw HTML

            <details>
            <summary>Expand</summary>

            Hidden details content.

            </details>

            <u>Underlined via HTML</u>
            """;

        var create = await _client.PostAsJsonAsync("/api/notes", new { title, folder = "", markdown = body });
        var note = await create.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);
        try
        {
            var get = await _client.GetFromJsonAsync<NoteDetailDto>($"/api/notes/{note!.Id}");
            Assert.NotNull(get);
            // Content-changing save (append a line) must keep raw HTML intact
            var next = get!.Markdown.TrimEnd() + "\n\nTrailing line.\n";
            var save = await _client.PutAsJsonAsync($"/api/notes/{note.Id}", new
            {
                markdown = next,
                etag = get.ETag
            });
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var saved = await save.Content.ReadFromJsonAsync<SaveResultDto>();
            Assert.True(saved!.Success);
            Assert.Contains("<details>", saved.Note!.Markdown, StringComparison.Ordinal);
            Assert.Contains("<summary>Expand</summary>", saved.Note.Markdown, StringComparison.Ordinal);
            Assert.Contains("<u>Underlined via HTML</u>", saved.Note.Markdown, StringComparison.Ordinal);
            Assert.Contains("Trailing line.", saved.Note.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Duplicate_titles_never_overwrite()
    {
        var title = "DupTitle " + Guid.NewGuid().ToString("N")[..6];
        var a = await _client.PostAsJsonAsync("/api/notes", new { title, folder = "", markdown = "# A\n" });
        var b = await _client.PostAsJsonAsync("/api/notes", new { title, folder = "", markdown = "# B\n" });
        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        var noteA = await a.Content.ReadFromJsonAsync<NoteDetailDto>();
        var noteB = await b.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(noteA);
        Assert.NotNull(noteB);
        try
        {
            Assert.NotEqual(noteA!.Id, noteB!.Id);
            var settings = await _client.GetFromJsonAsync<VaultSettingsDto>("/api/settings/vault");
            Assert.NotNull(settings?.VaultPath);
            Assert.True(File.Exists(Path.Combine(settings!.VaultPath!, title + ".md")));
            Assert.True(File.Exists(Path.Combine(settings.VaultPath!, title + " (1).md")));
            Assert.Contains("# A", noteA.Markdown, StringComparison.Ordinal);
            Assert.Contains("# B", noteB.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{noteA!.Id}");
            await _client.DeleteAsync($"/api/notes/{noteB!.Id}");
        }
    }

    [Fact]
    public async Task Auth_status_reports_development_bypass()
    {
        var status = await _client.GetFromJsonAsync<AuthStatusDto>("/api/auth/status");
        Assert.NotNull(status);
        Assert.True(status!.DevelopmentBypass);
        Assert.False(status.AuthRequired);
        Assert.False(status.SetupRequired);
    }

    [Fact]
    public async Task Static_export_writes_index_and_note_html()
    {
        var res = await _client.PostAsync("/api/admin/export-static", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<ExportDto>();
        Assert.NotNull(json);
        Assert.True(json!.Success);
        Assert.False(string.IsNullOrWhiteSpace(json.ExportPath));
        Assert.True(json.NoteCount >= 1);
        Assert.True(Directory.Exists(json.ExportPath));
        Assert.True(File.Exists(Path.Combine(json.ExportPath!, "index.html")));
        Assert.True(File.Exists(Path.Combine(json.ExportPath!, "site.css")));
        Assert.True(File.Exists(Path.Combine(json.ExportPath!, "search", "index.json")));
        var htmlFiles = Directory.GetFiles(json.ExportPath!, "*.html", SearchOption.AllDirectories);
        Assert.True(htmlFiles.Length >= 2); // index + at least one note
        var sample = await File.ReadAllTextAsync(htmlFiles.First(f => !f.EndsWith("index.html", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain("<script>alert", sample, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Integrity_scan_returns_ok_for_sample_vault()
    {
        var res = await _client.GetAsync("/api/admin/integrity");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<IntegrityDto>();
        Assert.NotNull(json);
        Assert.True(json!.Success);
        Assert.True(json.NoteCount >= 1);
    }

    [Fact]
    public async Task Diagnostics_reports_data_root()
    {
        var res = await _client.GetAsync("/api/admin/diagnostics");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("dataRoot", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mirror_settings_reject_same_as_vault_path()
    {
        var settings = await _client.GetFromJsonAsync<VaultSettingsDto>("/api/settings/vault");
        Assert.NotNull(settings?.VaultPath);
        var res = await _client.PutAsJsonAsync("/api/settings/mirror", new
        {
            enabled = true,
            destinationPath = settings!.VaultPath,
            intervalMinutes = 15
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Backup_bundle_creates_zip()
    {
        var res = await _client.PostAsync("/api/admin/backup?includeAuth=false&includeHistory=false", null);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<BackupDto>();
        Assert.NotNull(json);
        Assert.True(json!.Success);
        Assert.False(string.IsNullOrWhiteSpace(json.BundlePath));
        Assert.True(File.Exists(json.BundlePath));
        Assert.True(json.Bytes > 0);
    }

    private sealed class BackupDto
    {
        public bool Success { get; set; }
        public string? BundlePath { get; set; }
        public long Bytes { get; set; }
    }

    private sealed class IntegrityDto
    {
        public bool Success { get; set; }
        public int NoteCount { get; set; }
        public int IssueCount { get; set; }
    }

    private sealed class ExportDto
    {
        public bool Success { get; set; }
        public string? ExportPath { get; set; }
        public int NoteCount { get; set; }
    }

    private sealed class AuthStatusDto
    {
        public bool SetupComplete { get; set; }
        public bool Authenticated { get; set; }
        public bool AuthRequired { get; set; }
        public bool SetupRequired { get; set; }
        public bool DevelopmentBypass { get; set; }
    }

    private sealed class VaultDto
    {
        public bool Configured { get; set; }
        public int NoteCount { get; set; }
    }

    private sealed class NoteDto
    {
        public Guid Id { get; set; }
    }

    private sealed class NoteSummaryDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public bool IsCodeSnippet { get; set; }
    }

    private sealed class SnippetListDto
    {
        public List<SnippetItemDto>? Items { get; set; }
    }

    private sealed class SnippetCreateDto
    {
        public SnippetItemDto? Snippet { get; set; }
    }

    private sealed class SnippetItemDto
    {
        public Guid NoteId { get; set; }
        public string Title { get; set; } = "";
        public string Trigger { get; set; } = "";
        public string FolderPath { get; set; } = "";
    }

    private sealed class IndexDto
    {
        public bool Ready { get; set; }
        public bool Fts5 { get; set; }
        public bool Trigram { get; set; }
        public int NoteCount { get; set; }
    }

    private sealed class SearchDto
    {
        public string Mode { get; set; } = "";
        public List<HitDto> Hits { get; set; } = [];
    }

    private sealed class HitDto
    {
        public Guid NoteId { get; set; }
        public string Title { get; set; } = "";
    }

    private sealed class FolderDto
    {
        public bool Success { get; set; }
        public string? Path { get; set; }
    }

    private sealed class NoteDetailDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string Markdown { get; set; } = "";
        public string ETag { get; set; } = "";
    }

    private sealed class SaveResultDto
    {
        public bool Success { get; set; }
        public NoteDetailDto? Note { get; set; }
    }

    private sealed class MoveDto
    {
        public bool Success { get; set; }
        public NoteDetailDto? Note { get; set; }
    }

    private sealed class VaultSettingsDto
    {
        public string? VaultPath { get; set; }
    }

    private sealed class UploadDto
    {
        public bool Success { get; set; }
        public string? MarkdownPath { get; set; }
        public string? AttachmentId { get; set; }
        public string? FileName { get; set; }
        public bool IsImage { get; set; }
        public string? Error { get; set; }
    }
}
