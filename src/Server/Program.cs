using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jotdex.Core.CloudBackup;
using Jotdex.Core.Configuration;
using Jotdex.Core.Notifications;
using Jotdex.Core.Search;
using Jotdex.Core.Secrets;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Auth;
using Jotdex.Infrastructure.CloudBackup;
using Jotdex.Infrastructure.Config;
using Jotdex.Infrastructure.Export;
using Jotdex.Infrastructure.History;
using Jotdex.Infrastructure.Images;
using Jotdex.Infrastructure.Logging;
using Jotdex.Infrastructure.Maintenance;
using Jotdex.Infrastructure.Net;
using Jotdex.Infrastructure.Notifications;
using Jotdex.Infrastructure.Paths;
using Jotdex.Infrastructure.Search;
using Jotdex.Infrastructure.Secrets;
using Jotdex.Infrastructure.Vault;
using Jotdex.Server.Auth;
using Jotdex.Server.CloudBackup;
using Jotdex.Server.Hosting;
using Microsoft.Extensions.Options;

// Offline helper: Jotdex.Server.exe --decrypt-kit <kit.jotdexkit> <out.zip>
// Supports streaming JDXK2 and legacy JDXK1. Password via env JOTDEX_DECRYPT_PASSWORD or prompt.
if (args is ["--decrypt-kit", var kitPath, var outZip])
{
    var password = Environment.GetEnvironmentVariable("JOTDEX_DECRYPT_PASSWORD");
    if (string.IsNullOrEmpty(password))
    {
        Console.Write("Jotdex unlock password: ");
        password = ReadPassword();
        Console.WriteLine();
    }
    try
    {
        DecryptKitFile(kitPath, password!, outZip);
        Console.WriteLine("OK " + outZip);
        return;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
        return;
    }
}

static string ReadPassword()
{
    var sb = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (sb.Length > 0) sb.Length--;
            continue;
        }
        sb.Append(key.KeyChar);
    }
    return sb.ToString();
}

static void DecryptKitFile(string encryptedPath, string password, string outputZipPath)
{
    const int NonceSize = 12;
    const int NoncePrefixSize = 8;
    const int TagSize = 16;
    const int KeySize = 32;
    const int Pbkdf2Iterations = 200_000;
    const int ChunkSize = 4 * 1024 * 1024;

    using var fs = File.OpenRead(encryptedPath);
    using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
    var magic = Encoding.ASCII.GetString(br.ReadBytes(5));
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputZipPath))!);
    var tmp = outputZipPath + ".partial";
    try
    {
        if (magic == "JDXK2")
        {
            var saltLen = br.ReadUInt16();
            var salt = br.ReadBytes(saltLen);
            var wrapLen = br.ReadUInt16();
            var wrapped = br.ReadBytes(wrapLen);
            var plainLen = br.ReadInt64();
            var chunkCount = br.ReadUInt32();
            var noncePrefix = br.ReadBytes(NoncePrefixSize);
            if (noncePrefix.Length != NoncePrefixSize)
                throw new InvalidDataException("Invalid JDXK2 nonce prefix.");

            var kek = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
            if (wrapped.Length < NonceSize + TagSize + KeySize)
                throw new InvalidDataException("Invalid wrapped key.");
            var wNonce = wrapped.AsSpan(0, NonceSize);
            var wTag = wrapped.AsSpan(NonceSize, TagSize);
            var wCipher = wrapped.AsSpan(NonceSize + TagSize);
            var aesKey = new byte[wCipher.Length];
            using (var aesUnwrap = new AesGcm(kek, TagSize))
                aesUnwrap.Decrypt(wNonce, wCipher, wTag, aesKey);

            using var output = File.Create(tmp);
            using var aes = new AesGcm(aesKey, TagSize);
            long written = 0;
            var cipherBuf = new byte[ChunkSize];
            var plainBuf = new byte[ChunkSize];
            var tag = new byte[TagSize];
            for (uint i = 0; i < chunkCount; i++)
            {
                var chunkPlainLen = br.ReadUInt32();
                if (chunkPlainLen > ChunkSize)
                    throw new InvalidDataException("JDXK2 chunk too large.");
                var cipher = br.ReadBytes((int)chunkPlainLen);
                if (cipher.Length != chunkPlainLen)
                    throw new InvalidDataException("Truncated JDXK2 chunk.");
                if (br.Read(tag, 0, TagSize) != TagSize)
                    throw new InvalidDataException("Missing JDXK2 auth tag.");

                var nonce = new byte[NonceSize];
                Buffer.BlockCopy(noncePrefix, 0, nonce, 0, NoncePrefixSize);
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixSize), i);
                var aad = new byte[8];
                BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(0), i);
                BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(4), chunkPlainLen);
                var plainSpan = plainBuf.AsSpan(0, (int)chunkPlainLen);
                aes.Decrypt(nonce, cipher, tag, plainSpan, aad);
                output.Write(plainSpan);
                written += chunkPlainLen;
            }

            if (written != plainLen)
                throw new InvalidDataException("JDXK2 plaintext length mismatch.");
            if (br.BaseStream.Position != br.BaseStream.Length)
                throw new InvalidDataException("Extra data after JDXK2 payload.");
        }
        else if (magic == "JDXK1")
        {
            var saltLen = br.ReadUInt16();
            var salt = br.ReadBytes(saltLen);
            var wrapLen = br.ReadUInt16();
            var wrapped = br.ReadBytes(wrapLen);
            var nonce = br.ReadBytes(NonceSize);
            var tag = br.ReadBytes(TagSize);
            var cipher = br.ReadBytes((int)(fs.Length - fs.Position));

            var kek = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
            if (wrapped.Length < NonceSize + TagSize + KeySize)
                throw new InvalidDataException("Invalid wrapped key.");
            var wNonce = wrapped.AsSpan(0, NonceSize);
            var wTag = wrapped.AsSpan(NonceSize, TagSize);
            var wCipher = wrapped.AsSpan(NonceSize + TagSize);
            var aesKey = new byte[wCipher.Length];
            using (var aes = new AesGcm(kek, TagSize))
                aes.Decrypt(wNonce, wCipher, wTag, aesKey);

            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Decrypt(nonce, cipher, tag, plain);
            File.WriteAllBytes(tmp, plain);
        }
        else
        {
            throw new InvalidDataException("Not a Jotdex encrypted move kit.");
        }

        File.Move(tmp, outputZipPath, overwrite: true);
    }
    catch
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
        throw;
    }
}

var builder = WebApplication.CreateBuilder(args);
Jotdex.Server.Hosting.NetworkListenConfigurator.Apply(builder);

if (OperatingSystem.IsWindows())
    builder.Host.UseWindowsService();

builder.Services.Configure<JotdexOptions>(builder.Configuration.GetSection(JotdexOptions.SectionName));
builder.Services.AddSingleton<IDataRootResolver, DataRootResolver>();
builder.Services.AddSingleton<IVaultPathGuard, VaultPathGuard>();
builder.Services.AddSingleton<IMarkdownRenderer, MarkdigMarkdownRenderer>();
builder.Services.AddSingleton<SqliteSearchIndex>();
builder.Services.AddSingleton<ISearchIndex>(sp => sp.GetRequiredService<SqliteSearchIndex>());
builder.Services.AddSingleton<IVaultRescanObserver>(sp => sp.GetRequiredService<SqliteSearchIndex>());
builder.Services.AddSingleton<INoteHistoryService, NoteHistoryService>();
builder.Services.AddSingleton<INoteCommandService, NoteCommandService>();
builder.Services.AddSingleton<IVaultTaskService, VaultTaskService>();
builder.Services.AddSingleton<IFolderCommandService, FolderCommandService>();
builder.Services.AddSingleton<SafeRemoteImageClient>();
builder.Services.AddSingleton<SafeRemotePageClient>();
builder.Services.AddSingleton<IImageLocalizer, ImageLocalizer>();
builder.Services.AddSingleton<IPreservePageService, PreservePageService>();
builder.Services.AddSingleton<IStaticExportService, StaticExportService>();
builder.Services.AddSingleton<INoteShareExportService, NoteShareExportService>();
builder.Services.AddSingleton<IIntegrityScanService, IntegrityScanService>();
builder.Services.AddSingleton<IMaintenanceService, MaintenanceService>();
builder.Services.AddSingleton<ITrashBrowserService, TrashBrowserService>();
builder.Services.AddSingleton<IFirewallLanService, FirewallLanService>();
builder.Services.AddSingleton<IBackupBundleService, BackupBundleService>();
builder.Services.AddSingleton<IMoveKitService, MoveKitService>();
builder.Services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
builder.Services.AddHttpClient("github");
builder.Services.AddHttpClient("telegram");
builder.Services.AddSingleton<ISecretStore, DpapiSecretStore>();
builder.Services.AddSingleton<ILocalAuthProbe, LocalAuthProbe>();
builder.Services.AddSingleton<IMoveKitCryptoService, MoveKitCryptoService>();
builder.Services.AddSingleton<IUiPrefsService, UiPrefsService>();
builder.Services.AddSingleton<NotificationSettingsService>();
builder.Services.AddSingleton<INotificationSettingsService>(sp => sp.GetRequiredService<NotificationSettingsService>());
builder.Services.AddSingleton<IMirrorAlertState>(sp => sp.GetRequiredService<NotificationSettingsService>());
builder.Services.AddSingleton<IOpsAlertSender, OpsAlertSender>();
builder.Services.AddHostedService<PortableSecretsImportHostedService>();
builder.Services.AddHostedService<MirrorStaleAlertHostedService>();
builder.Services.AddHostedService<DailyMirrorMoveKitHostedService>();
builder.Services.AddSingleton<IVaultMirrorService, VaultMirrorService>();
builder.Services.AddSingleton<INoteLinkService, NoteLinkService>();
builder.Services.AddHostedService<VaultMirrorHostedService>();
builder.Services.AddSingleton<IVaultService, VaultService>();
builder.Services.AddHostedService<VaultBootstrapHostedService>();
builder.Services.AddHostedService<VaultFileWatcher>();
builder.Services.AddSingleton<IAppVersion>(_ => new AssemblyAppVersion());
builder.Services.AddSingleton(new StopwatchHolder(Stopwatch.StartNew()));
builder.Services.AddSingleton<Jotdex.Server.Hosting.IServerRestartService, Jotdex.Server.Hosting.ServerRestartService>();

// Cloud backup (multi-provider)
builder.Services.AddSingleton<ICloudBackupSettingsService, CloudBackupSettingsService>();
builder.Services.AddSingleton<ICloudBackupStateStore, CloudBackupStateStore>();
builder.Services.AddSingleton<ICloudCredentialStore, DpapiCloudCredentialStore>();
builder.Services.AddSingleton<CloudBackupHashService>();
builder.Services.AddSingleton<ICloudBackupSnapshotService, CloudBackupSnapshotService>();
builder.Services.AddSingleton<IVaultSnapshotZipService, VaultSnapshotZipService>();
builder.Services.AddSingleton<ICloudBackupArtifactService, CloudBackupArtifactService>();
builder.Services.AddSingleton<ICloudBackupHealthService, CloudBackupHealthService>();
builder.Services.AddSingleton<ICloudOAuthConnectionService, CloudOAuthConnectionService>();
CloudBackupProviderFactory.AddCloudBackupProviders(builder.Services);
builder.Services.AddSingleton<ICloudBackupCoordinator, CloudBackupCoordinator>();
builder.Services.AddHostedService<CloudBackupHostedService>();

builder.Services.AddJotdexAuth(builder.Configuration);

// Resolve data root early so file logs land next to other app data
var earlyOpts = builder.Configuration.GetSection(JotdexOptions.SectionName).Get<JotdexOptions>() ?? new JotdexOptions();
var earlyDataRoot = !string.IsNullOrWhiteSpace(earlyOpts.DataRoot)
    ? Path.GetFullPath(earlyOpts.DataRoot)
    : earlyOpts.PortableMode
        ? Path.Combine(builder.Environment.ContentRootPath, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jotdex");
Directory.CreateDirectory(earlyDataRoot);
if (builder.Environment.IsDevelopment())
{
    // Optional gitignored data/config/cloud-oauth.local.json → process env (real OAuth client IDs).
    // When unset, Development still gets local-folder provider fallbacks (see CloudBackupProviderResolver).
    CloudBackupLocalOAuthEnv.TryApply(earlyDataRoot, builder.Environment.ContentRootPath);
}
var fileLogs = new FileLoggerProvider(earlyDataRoot);
builder.Services.AddSingleton(fileLogs);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddProvider(fileLogs);
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

var uiPrefs = app.Services.GetRequiredService<IUiPrefsService>();
var cookieOpts = app.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>();
uiPrefs.BindCookieOptions(timeout =>
{
    cookieOpts.Get(Jotdex.Server.Auth.AuthEndpointExtensions.CookieScheme).ExpireTimeSpan = timeout;
});

app.UseAuthentication();
app.UseAuthorization();
app.UseJotdexAuthGate();

app.MapAuthEndpoints();
app.MapCloudBackupEndpoints();

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

app.MapGet("/api/notes/by-path", (IVaultService vault, IVaultPathGuard paths, string? path) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    if (string.IsNullOrWhiteSpace(path)) return Results.BadRequest(new { error = "path required" });
    var note = vault.GetNoteByRelativePath(path);
    return note is null ? Results.NotFound() : Results.Json(note);
});

app.MapGet("/api/notes/index", (INoteLinkService links, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    return Results.Json(new { notes = links.GetIndex() });
});

app.MapGet("/api/notes/{id:guid}", (Guid id, IVaultService vault, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var note = vault.GetNote(id);
    return note is null ? Results.NotFound() : Results.Json(note);
});

app.MapGet("/api/notes/{id:guid}/backlinks", (Guid id, INoteLinkService links, IVaultService vault, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    if (vault.GetNote(id) is null) return Results.NotFound();
    return Results.Json(new { links = links.GetBacklinks(id) });
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

app.MapGet("/api/admin/logs", (FileLoggerProvider logs, int? lines) =>
{
    var tail = logs.ReadTail(lines ?? 250);
    return Results.Json(new
    {
        logsDirectory = logs.LogsDirectory,
        latestLogPath = logs.LatestLogPath,
        text = tail
    });
});

app.MapGet("/api/admin/autostart", (IHostEnvironment env) =>
{
    var info = AutostartHelper.GetStatus(env.ContentRootPath);
    return Results.Json(info);
});

app.MapPost("/api/admin/autostart", async (HttpRequest request, IHostEnvironment env) =>
{
    var body = await request.ReadFromJsonAsync<AutostartBody>();
    var enable = body?.Enabled ?? true;
    var (ok, error, status) = AutostartHelper.SetUserStartupShortcut(enable, env.ContentRootPath);
    return ok
        ? Results.Json(new { success = true, status })
        : Results.BadRequest(new { success = false, error });
});

app.MapPost("/api/clip", async (HttpRequest request, INoteCommandService commands) =>
{
    var body = await request.ReadFromJsonAsync<ClipBody>();
    if (body is null) return Results.BadRequest(new { error = "Invalid body" });

    var text = body.Text ?? body.Markdown ?? "";
    if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(body.Html) &&
        string.IsNullOrWhiteSpace(body.SourceUrl))
        return Results.BadRequest(new { error = "text, html, or sourceUrl required" });

    var title = string.IsNullOrWhiteSpace(body.Title)
        ? $"Capture {DateTime.Now:yyyy-MM-dd HH:mm}"
        : body.Title.Trim();

    var folder = (body.Folder ?? "Inbox").Replace('\\', '/').Trim().Trim('/');
    var folderArg = folder;

    var sb = new StringBuilder();
    sb.Append("# ").Append(title).AppendLine().AppendLine();
    if (!string.IsNullOrWhiteSpace(body.SourceUrl))
        sb.Append("> Source: ").Append(body.SourceUrl.Trim()).AppendLine().AppendLine();
    if (!string.IsNullOrWhiteSpace(text))
        sb.AppendLine(text.Trim());
    else if (!string.IsNullOrWhiteSpace(body.SourceUrl) && string.IsNullOrWhiteSpace(body.Html))
        sb.AppendLine(body.SourceUrl.Trim());
    if (!string.IsNullOrWhiteSpace(body.Html))
    {
        sb.AppendLine().AppendLine("<details><summary>Clipped HTML</summary>").AppendLine();
        sb.AppendLine("```html");
        sb.AppendLine(body.Html.Trim());
        sb.AppendLine("```");
        sb.AppendLine("</details>");
    }

    var note = commands.Create(folderArg, title, sb.ToString());
    if (note is null) return Results.BadRequest(new { error = "Could not create note (check folder path)" });
    return Results.Json(new { success = true, noteId = note.Id, relativePath = note.RelativePath, folder = folderArg });
});

app.MapPost("/api/fetch-page", async (HttpRequest request, SafeRemotePageClient pages, CancellationToken ct) =>
{
    var body = await request.ReadFromJsonAsync<FetchPageBody>();
    if (string.IsNullOrWhiteSpace(body?.Url)) return Results.BadRequest(new { error = "url required" });
    var result = await pages.FetchAsync(body.Url, ct);
    return result.Success
        ? Results.Json(new
        {
            success = true,
            title = result.Title,
            description = result.Description,
            textExcerpt = result.TextExcerpt,
            finalUrl = result.FinalUrl ?? body.Url
        })
        : Results.BadRequest(new { success = false, error = result.Error ?? "Fetch failed" });
});

app.MapGet("/api/tasks", (IVaultTaskService tasks) => Results.Json(new { items = tasks.ListOpenTasks() }));

app.MapPost("/api/tasks/complete", async (HttpRequest request, IVaultTaskService tasks) =>
{
    var body = await request.ReadFromJsonAsync<TrashIdBody>();
    if (string.IsNullOrWhiteSpace(body?.Id)) return Results.BadRequest(new { error = "id required" });
    var result = tasks.Complete(body.Id);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/tasks/update", async (HttpRequest request, IVaultTaskService tasks) =>
{
    var body = await request.ReadFromJsonAsync<TaskUpdateBody>();
    if (string.IsNullOrWhiteSpace(body?.Id)) return Results.BadRequest(new { error = "id required" });
    var result = tasks.Update(body.Id, new VaultTaskUpdate
    {
        Text = body.Text,
        Priority = body.Priority,
        Due = body.Due,
        Remind = body.Remind
    });
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapGet("/api/trash", (ITrashBrowserService trash) => Results.Json(new { items = trash.List() }));

app.MapPost("/api/trash/restore", async (HttpRequest request, ITrashBrowserService trash) =>
{
    var body = await request.ReadFromJsonAsync<TrashRestoreBody>();
    if (body?.Id is null) return Results.BadRequest(new { error = "id required" });
    var result = trash.Restore(body.Id, body.AsCopy);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapPost("/api/trash/delete", async (HttpRequest request, ITrashBrowserService trash) =>
{
    var body = await request.ReadFromJsonAsync<TrashIdBody>();
    if (body?.Id is null) return Results.BadRequest(new { error = "id required" });
    var result = trash.DeletePermanent(body.Id);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

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

app.MapPost("/api/admin/move-kit", async (HttpRequest request, IMoveKitService moveKit, CancellationToken ct) =>
{
    var includeAuth = !string.Equals(request.Query["includeAuth"], "false", StringComparison.OrdinalIgnoreCase);
    var includeHistory = !string.Equals(request.Query["includeHistory"], "false", StringComparison.OrdinalIgnoreCase);
    string? password = null;
    try
    {
        if (request.ContentLength is > 0 ||
            (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            var body = await request.ReadFromJsonAsync<MoveKitBody>();
            password = body?.Password;
            if (body?.IncludeAuth is bool ia) includeAuth = ia;
            if (body?.IncludeHistory is bool ih) includeHistory = ih;
        }
    }
    catch { /* optional body */ }

    var result = await moveKit.CreateAsync(includeAuth, includeHistory, password, null, ct);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.MapGet("/api/updates/check", async (IUpdateCheckService updates, CancellationToken ct) =>
{
    var result = await updates.CheckAsync(ct);
    return result.Success ? Results.Json(result) : Results.Json(result, statusCode: StatusCodes.Status502BadGateway);
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

app.MapPost("/api/notes/{id:guid}/import-image", async (Guid id, HttpRequest request, IImageLocalizer localizer, IVaultPathGuard paths, CancellationToken ct) =>
{
    if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
    var body = await request.ReadFromJsonAsync<ImportImageBody>(cancellationToken: ct);
    if (body is null || string.IsNullOrWhiteSpace(body.Url))
        return Results.BadRequest(new { success = false, error = "url required" });
    var result = await localizer.ImportOneAsync(id, body.Url, ct);
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

app.MapGet("/api/notes/{id:guid}/history", (Guid id, INoteHistoryService history, IVaultService vault, IVaultPathGuard paths) =>
{
    if (!paths.IsConfigured) return Results.NotFound();
    var current = vault.GetNote(id)?.Markdown;
    return Results.Json(history.ListWithSummaries(id, current));
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

internal sealed class ImportImageBody
{
    public string? Url { get; set; }
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

internal sealed class AutostartBody
{
    public bool Enabled { get; set; } = true;
}

internal sealed class TrashRestoreBody
{
    public string? Id { get; set; }
    public bool AsCopy { get; set; }
}

internal sealed class TrashIdBody
{
    public string? Id { get; set; }
}

internal sealed class TaskUpdateBody
{
    public string? Id { get; set; }
    public string? Text { get; set; }
    public string? Priority { get; set; }
    public string? Due { get; set; }
    public string? Remind { get; set; }
}

internal sealed class ClipBody
{
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? Markdown { get; set; }
    public string? Html { get; set; }
    public string? SourceUrl { get; set; }
    /// <summary>Vault-relative folder (e.g. Inbox). Empty = vault root.</summary>
    public string? Folder { get; set; }
}

internal sealed class FetchPageBody
{
    public string? Url { get; set; }
}

internal sealed class MoveKitBody
{
    public string? Password { get; set; }
    public bool? IncludeAuth { get; set; }
    public bool? IncludeHistory { get; set; }
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
