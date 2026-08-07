using System.Diagnostics;
using System.Reflection;
using Jotdex.Core.Configuration;
using Jotdex.Core.Search;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Config;
using Jotdex.Infrastructure.Export;
using Jotdex.Infrastructure.History;
using Jotdex.Infrastructure.Images;
using Jotdex.Infrastructure.Maintenance;
using Jotdex.Infrastructure.Net;
using Jotdex.Infrastructure.Paths;
using Jotdex.Infrastructure.Search;
using Jotdex.Infrastructure.Vault;
using Jotdex.Server.Auth;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
Jotdex.Server.Hosting.NetworkListenConfigurator.Apply(builder);

builder.Services.Configure<JotdexOptions>(builder.Configuration.GetSection(JotdexOptions.SectionName));
builder.Services.AddSingleton<IDataRootResolver, DataRootResolver>();
builder.Services.AddSingleton<IVaultPathGuard, VaultPathGuard>();
builder.Services.AddSingleton<IMarkdownRenderer, MarkdigMarkdownRenderer>();
builder.Services.AddSingleton<SqliteSearchIndex>();
builder.Services.AddSingleton<ISearchIndex>(sp => sp.GetRequiredService<SqliteSearchIndex>());
builder.Services.AddSingleton<IVaultRescanObserver>(sp => sp.GetRequiredService<SqliteSearchIndex>());
builder.Services.AddSingleton<INoteHistoryService, NoteHistoryService>();
builder.Services.AddSingleton<INoteCommandService, NoteCommandService>();
builder.Services.AddSingleton<IFolderCommandService, FolderCommandService>();
builder.Services.AddSingleton<SafeRemoteImageClient>();
builder.Services.AddSingleton<IImageLocalizer, ImageLocalizer>();
builder.Services.AddSingleton<IPreservePageService, PreservePageService>();
builder.Services.AddSingleton<IStaticExportService, StaticExportService>();
builder.Services.AddSingleton<INoteShareExportService, NoteShareExportService>();
builder.Services.AddSingleton<IIntegrityScanService, IntegrityScanService>();
builder.Services.AddSingleton<IMaintenanceService, MaintenanceService>();
builder.Services.AddSingleton<IBackupBundleService, BackupBundleService>();
builder.Services.AddSingleton<IVaultMirrorService, VaultMirrorService>();
builder.Services.AddHostedService<VaultMirrorHostedService>();
builder.Services.AddSingleton<IVaultService, VaultService>();
builder.Services.AddHostedService<VaultBootstrapHostedService>();
builder.Services.AddHostedService<VaultFileWatcher>();
builder.Services.AddSingleton<IAppVersion>(_ => new AssemblyAppVersion());
builder.Services.AddSingleton(new StopwatchHolder(Stopwatch.StartNew()));
builder.Services.AddSingleton<Jotdex.Server.Hosting.IServerRestartService, Jotdex.Server.Hosting.ServerRestartService>();
builder.Services.AddJotdexAuth(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

var app = builder.Build();

_ = app.Services.GetRequiredService<IDataRootResolver>().ResolveDataRoot();
app.Logger.LogInformation("Jotdex data root ready");
var vaultGuard = app.Services.GetRequiredService<IVaultPathGuard>();
app.Logger.LogInformation("Vault configured: {Configured}", vaultGuard.IsConfigured);

app.Use(async (ctx, next) =>
{
    // Baseline CSP for M5 — tighten further with nonces when auth lands
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseJotdexAuthGate();

app.MapAuthEndpoints();

app.MapGet("/api/health", (IAppVersion version, IDataRootResolver paths, IVaultPathGuard vault, ISearchIndex search, StopwatchHolder uptime, IOptions<JotdexOptions> options) =>
{
    var index = search.Probe();
    return Results.Json(new
    {
        status = "ok",
        version = version.Version,
        uptimeSeconds = Math.Round(uptime.Stopwatch.Elapsed.TotalSeconds, 1),
        vaultConfigured = vault.IsConfigured,
        portableMode = options.Value.PortableMode,
        dataRootConfigured = !string.IsNullOrWhiteSpace(paths.ResolveDataRoot()),
        search = new { index.Ready, index.Fts5, index.Trigram, index.NoteCount }
    });
});

app.MapGet("/api/vault", (IVaultService vault, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.Json(new { configured = false });
    var info = vault.GetInfo();
    return Results.Json(new
    {
        configured = true,
        name = info.Name,
        formatVersion = info.FormatVersion,
        noteCount = info.NoteCount,
        folderCount = info.FolderCount
    });
});

app.MapGet("/api/tree", (IVaultService vault, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    return Results.Json(vault.GetTree());
});

app.MapGet("/api/notes", (IVaultService vault, IVaultPathGuard paths, string? folder) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    return Results.Json(vault.ListNotes(folder));
});

app.MapGet("/api/notes/{id:guid}", (Guid id, IVaultService vault, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var note = vault.GetNote(id);
    return note is null ? Results.NotFound() : Results.Json(note);
});

app.MapGet("/api/attachments/{attachmentId}", (string attachmentId, IVaultService vault, IVaultPathGuard paths, HttpResponse response) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    var info = vault.GetAttachment(attachmentId);
    if (info is null) return Results.NotFound();

    var inline = info.ContentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
    response.Headers.Append("X-Content-Type-Options", "nosniff");
    response.Headers.Append("Content-Disposition",
        $"{(inline ? "inline" : "attachment")}; filename=\"{info.FileName}\"");

    var stream = vault.OpenAttachmentStream(attachmentId);
    return Results.File(stream, inline ? info.ContentType : "application/octet-stream", fileDownloadName: inline ? null : info.FileName);
});

app.MapGet("/api/attachments/{attachmentId}/text", async (string attachmentId, IVaultService vault, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    var info = vault.GetAttachment(attachmentId);
    if (info is null) return Results.NotFound();
    if (!info.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
        info.ContentType is not "application/json")
        return Results.BadRequest(new { error = "Not a text attachment" });

    await using var stream = vault.OpenAttachmentStream(attachmentId);
    using var reader = new StreamReader(stream);
    var text = await reader.ReadToEndAsync();
    return Results.Json(new { fileName = info.FileName, content = text });
});

app.MapGet("/api/search", (string? q, ISearchIndex search, IVaultPathGuard paths, bool literal = false) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var result = search.Search(new SearchRequest
    {
        RawQuery = q ?? "",
        ForceLiteral = literal
    });
    return Results.Json(result);
});

app.MapGet("/api/admin/reindex/status", (ISearchIndex search) => Results.Json(search.Probe()));

app.MapPost("/api/admin/reindex", (ISearchIndex search, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    search.RebuildFromVault();
    return Results.Json(search.Probe());
});

app.MapPost("/api/admin/rescan", (IVaultService vault, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    vault.Rescan();
    return Results.Json(vault.GetInfo());
});

app.MapPost("/api/admin/export-static", (IStaticExportService export, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var result = export.Export();
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapGet("/api/admin/integrity", (IIntegrityScanService integrity, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var result = integrity.Scan();
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapGet("/api/admin/diagnostics", (IMaintenanceService maintenance) => Results.Json(maintenance.GetDiagnostics()));

app.MapPost("/api/admin/trash/empty", (IMaintenanceService maintenance, HttpRequest request) =>
{
    var olderOnly = string.Equals(request.Query["olderThanDaysOnly"], "true", StringComparison.OrdinalIgnoreCase);
    var days = 30;
    if (int.TryParse(request.Query["days"], out var d)) days = d;
    var result = maintenance.EmptyTrash(olderOnly, days);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/admin/backup", async (HttpRequest request, IBackupBundleService backup, CancellationToken ct) =>
{
    var includeAuth = !string.Equals(request.Query["includeAuth"], "false", StringComparison.OrdinalIgnoreCase);
    var includeHistory = !string.Equals(request.Query["includeHistory"], "false", StringComparison.OrdinalIgnoreCase);
    var result = await backup.CreateAsync(includeAuth, includeHistory, ct);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/admin/restart", (Jotdex.Server.Hosting.IServerRestartService restart) =>
{
    var (ok, error, message) = restart.ScheduleRestart();
    return ok
        ? Results.Json(new { success = true, message, restarting = true })
        : Results.BadRequest(new { error });
});

app.MapGet("/api/settings/vault", (IVaultPathGuard paths) =>
{
    return Results.Json(new
    {
        configured = paths.IsConfigured,
        vaultPath = paths.IsConfigured ? paths.VaultRoot : null,
        hint = "Use a local-disk folder (e.g. C:\\JotdexVault). Avoid iCloud for the live vault."
    });
});

app.MapPut("/api/settings/vault", async (HttpRequest request, IVaultPathGuard paths, IVaultService vault) =>
{
    var body = await request.ReadFromJsonAsync<VaultSettingsBody>();
    if (body is null || string.IsNullOrWhiteSpace(body.VaultPath))
        return Results.BadRequest(new { error = "vaultPath required" });

    var err = paths.TrySetVaultPath(body.VaultPath.Trim());
    if (err is not null)
        return Results.BadRequest(new { error = err });

    vault.Rescan();
    var info = vault.GetInfo();
    return Results.Json(new
    {
        configured = true,
        vaultPath = paths.VaultRoot,
        noteCount = info.NoteCount,
        folderCount = info.FolderCount,
        name = info.Name
    });
});

app.MapGet("/api/settings/browse", (string? path) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new { name = d.Name, path = d.RootDirectory.FullName, type = "drive" })
                .ToList();
            return Results.Json(new { path = "", entries = drives });
        }

        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
            return Results.BadRequest(new { error = "Path not found" });

        var parent = Directory.GetParent(full)?.FullName;
        var dirs = Directory.EnumerateDirectories(full)
            .Select(d =>
            {
                var name = Path.GetFileName(d);
                return new { name, path = d, type = "dir" };
            })
            .Where(e => !string.IsNullOrEmpty(e.name) && !e.name.StartsWith('.'))
            .OrderBy(e => e.name, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();

        return Results.Json(new { path = full, parent, entries = dirs });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/notes/{id:guid}", async (Guid id, HttpRequest request, INoteCommandService commands, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var body = await request.ReadFromJsonAsync<SaveNoteBody>();
    if (body is null || body.Markdown is null)
        return Results.BadRequest(new { error = "markdown required" });

    var etag = body.ETag ?? request.Headers.IfMatch.FirstOrDefault()?.Trim('"') ?? "";
    var result = commands.Save(id, body.Markdown, etag, body.Force);
    if (result.Conflict) return Results.Json(result, statusCode: StatusCodes.Status409Conflict);
    if (!result.Success) return Results.BadRequest(result);
    return Results.Json(result);
});

app.MapPost("/api/notes/{id:guid}/move", async (Guid id, HttpRequest request, INoteCommandService commands, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var body = await request.ReadFromJsonAsync<MoveNoteBody>();
    if (body is null) return Results.BadRequest(new { error = "body required" });
    var result = commands.Move(id, body.Folder ?? "", body.Title);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/notes/{id:guid}/duplicate", (Guid id, INoteCommandService commands, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    var note = commands.Duplicate(id);
    return note is null ? Results.BadRequest(new { error = "Duplicate failed" }) : Results.Json(note);
});

app.MapPost("/api/folders", async (HttpRequest request, IFolderCommandService folders, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var body = await request.ReadFromJsonAsync<CreateFolderBody>();
    if (body is null || string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest(new { error = "name required" });
    var result = folders.Create(body.Parent ?? "", body.Name);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPatch("/api/folders", async (HttpRequest request, IFolderCommandService folders, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var body = await request.ReadFromJsonAsync<PatchFolderBody>();
    if (body is null || string.IsNullOrWhiteSpace(body.Path))
        return Results.BadRequest(new { error = "path required" });

    FolderCommandResult result;
    if (!string.IsNullOrWhiteSpace(body.NewName))
        result = folders.Rename(body.Path, body.NewName);
    else if (body.NewParent is not null)
        result = folders.Move(body.Path, body.NewParent);
    else
        return Results.BadRequest(new { error = "newName or newParent required" });

    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapDelete("/api/folders", (string path, IFolderCommandService folders, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest(new { error = "path required" });
    var result = folders.Delete(path);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/notes/{id:guid}/attachments", async (Guid id, HttpRequest request, INoteCommandService commands, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "multipart form required" });

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "file required" });

    await using var stream = file.OpenReadStream();
    var result = commands.AddAttachment(id, stream, file.FileName, file.ContentType);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/notes/{id:guid}/localize-images", async (Guid id, HttpRequest request, IImageLocalizer localizer, IVaultPathGuard paths, CancellationToken ct) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var body = await request.ReadFromJsonAsync<LocalizeImagesBody>(cancellationToken: ct);
    var result = await localizer.LocalizeAsync(id, body?.Urls, body?.ETag, ct);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/notes/{id:guid}/preserve-page", async (Guid id, HttpRequest request, IPreservePageService preserve, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var body = await request.ReadFromJsonAsync<PreservePageBody>();
    if (body is null || string.IsNullOrWhiteSpace(body.Html))
        return Results.BadRequest(new { error = "html required" });
    var result = preserve.SaveClip(id, body.Html, body.SourceUrl, body.ETag);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/notes", async (HttpRequest request, INoteCommandService commands, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var body = await request.ReadFromJsonAsync<CreateNoteBody>();
    if (body is null || string.IsNullOrWhiteSpace(body.Title))
        return Results.BadRequest(new { error = "title required" });
    var note = commands.Create(body.Folder ?? "", body.Title, body.Markdown);
    return note is null ? Results.BadRequest() : Results.Json(note);
});

app.MapDelete("/api/notes/{id:guid}", (Guid id, INoteCommandService commands, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    return commands.MoveToTrash(id) ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/notes/{id:guid}/history", (Guid id, INoteHistoryService history, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    return Results.Json(history.List(id));
});

app.MapGet("/api/notes/{id:guid}/export-html", (Guid id, INoteShareExportService share, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var result = share.ExportSelfContainedHtml(id);
    if (!result.Success || string.IsNullOrEmpty(result.Html) || string.IsNullOrEmpty(result.FileName))
        return Results.BadRequest(new { success = false, error = result.Error ?? "Export failed" });

    var bytes = System.Text.Encoding.UTF8.GetBytes(result.Html);
    return Results.File(bytes, "text/html; charset=utf-8", result.FileName);
});

app.MapGet("/api/notes/{id:guid}/history/{snapshotId}", (Guid id, string snapshotId, INoteHistoryService history, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    var text = history.ReadSnapshot(id, snapshotId);
    return text is null ? Results.NotFound() : Results.Json(new { snapshotId, markdown = text });
});

app.MapPost("/api/notes/{id:guid}/history/{snapshotId}/restore", (Guid id, string snapshotId, INoteCommandService commands, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    var result = commands.RestoreHistory(id, snapshotId);
    if (result.Conflict) return Results.Json(result, statusCode: StatusCodes.Status409Conflict);
    if (!result.Success) return Results.BadRequest(result);
    return Results.Json(result);
});

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;

internal sealed class SaveNoteBody
{
    public string? Markdown { get; set; }
    public string? ETag { get; set; }
    public bool Force { get; set; }
}

internal sealed class CreateNoteBody
{
    public string Title { get; set; } = "";
    public string? Folder { get; set; }
    public string? Markdown { get; set; }
}

internal sealed class MoveNoteBody
{
    public string? Folder { get; set; }
    public string? Title { get; set; }
}

internal sealed class CreateFolderBody
{
    public string Name { get; set; } = "";
    public string? Parent { get; set; }
}

internal sealed class PatchFolderBody
{
    public string Path { get; set; } = "";
    public string? NewName { get; set; }
    public string? NewParent { get; set; }
}

internal sealed class LocalizeImagesBody
{
    public List<string>? Urls { get; set; }
    public string? ETag { get; set; }
}

internal sealed class PreservePageBody
{
    public string Html { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string? ETag { get; set; }
}

internal sealed class VaultSettingsBody
{
    public string VaultPath { get; set; } = "";
}

internal sealed class AssemblyAppVersion : IAppVersion
{
    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
}

internal sealed class StopwatchHolder(Stopwatch stopwatch)
{
    public Stopwatch Stopwatch { get; } = stopwatch;
}
