using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Jotdex.Core.Auth;
using Jotdex.Core.Configuration;
using Jotdex.Core.Integrations;
using Jotdex.Core.Search;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Export;
using Jotdex.Infrastructure.Integrations;
using Jotdex.Infrastructure.Vault;
using Jotdex.Server.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Jotdex.Server.Integrations;

public static class IntegrationEndpointExtensions
{
    public const string ReadRatePolicy = "integration-read";
    public const string WriteRatePolicy = "integration-write";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IServiceCollection AddJotdexIntegrations(this IServiceCollection services)
    {
        services.AddSingleton<IIntegrationConfigService, IntegrationConfigService>();
        services.AddSingleton<IIntegrationTokenStore, IntegrationTokenStore>();
        services.AddSingleton<IIntegrationFolderAccess, IntegrationFolderAccess>();
        services.AddSingleton<IIntegrationActivityLog, IntegrationActivityLog>();
        services.AddSingleton<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<IVaultWriteCoordinator, VaultWriteCoordinator>();
        services.AddSingleton<IVaultIdentity, VaultIdentityService>();
        services.AddSingleton(TimeProvider.System);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                ctx.HttpContext.Response.Headers.CacheControl = "no-store";
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Rate limit exceeded", code = "rate_limited" }, ct);
            };
            options.AddPolicy(ReadRatePolicy, httpContext =>
            {
                var key = httpContext.User.FindFirstValue(IntegrationAuth.ClaimTokenId) ?? "anon";
                var limit = httpContext.RequestServices.GetRequiredService<IIntegrationConfigService>().Get().ReadRequestsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(key + ":r", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
            options.AddPolicy(WriteRatePolicy, httpContext =>
            {
                var key = httpContext.User.FindFirstValue(IntegrationAuth.ClaimTokenId) ?? "anon";
                var limit = httpContext.RequestServices.GetRequiredService<IIntegrationConfigService>().Get().WriteRequestsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(key + ":w", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
        });
        return services;
    }

    public static void MapIntegrationEndpoints(this WebApplication app)
    {
        MapAdmin(app);
        MapExternal(app);
    }

    private static void MapAdmin(WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/integrations")
            .RequireAuthorization(IntegrationAuth.AdminPolicy);

        admin.MapGet("/csrf", (HttpContext ctx, IAntiforgery af) =>
        {
            var tokens = af.GetAndStoreTokens(ctx);
            return Results.Json(new { token = tokens.RequestToken });
        });

        admin.MapGet("/", (IIntegrationConfigService cfg, IIntegrationTokenStore tokens, IIntegrationActivityLog log) =>
        {
            var c = cfg.Get();
            return Results.Json(new
            {
                enabled = c.Enabled,
                readRequestsPerMinute = c.ReadRequestsPerMinute,
                writeRequestsPerMinute = c.WriteRequestsPerMinute,
                tokens = tokens.List(),
                recentActivity = log.Recent(30),
                setupGuide = SetupGuidePlaceholder()
            });
        });

        admin.MapPut("/config", async (HttpContext ctx, IAntiforgery af, IIntegrationConfigService cfg, ILocalAuthService auth, HttpRequest req) =>
        {
            await af.ValidateRequestAsync(ctx);
            if (!auth.IsSetupComplete)
                return Results.BadRequest(new { error = "Set a local administrator password before enabling Integrations." });
            var body = await req.ReadFromJsonAsync<ConfigBody>();
            if (body is null) return Results.BadRequest(new { error = "Invalid body" });
            var current = cfg.Get();
            if (body.Enabled == true) current.Enabled = true;
            if (body.Enabled == false) current.Enabled = false;
            if (body.ReadRequestsPerMinute is int r) current.ReadRequestsPerMinute = r;
            if (body.WriteRequestsPerMinute is int w) current.WriteRequestsPerMinute = w;
            cfg.Save(current);
            return Results.Json(new { success = true, enabled = current.Enabled });
        });

        admin.MapPost("/tokens", async (HttpContext ctx, IAntiforgery af, IIntegrationTokenStore store, IVaultIdentity vaultId,
            IIntegrationConfigService cfg, ILocalAuthService auth, TimeProvider time, HttpRequest req) =>
        {
            await af.ValidateRequestAsync(ctx);
            if (!auth.IsSetupComplete)
                return Results.BadRequest(new { error = "Set a local administrator password first." });
            var body = await req.ReadFromJsonAsync<CreateTokenBody>();
            if (body is null) return Results.BadRequest(new { error = "Invalid body" });
            var reauth = auth.VerifyAdminReauth(body.AdminPassword ?? "", body.TotpCode);
            if (!reauth.Success)
                // 403 — session is still valid; wrong password must not look like cookie expiry (IdleLock).
                return Results.Json(new { error = reauth.Error ?? "Administrator reauthentication required", code = "reauth_required", requiresTotp = reauth.RequiresTotp }, statusCode: 403);

            var vid = vaultId.GetVaultId();
            if (string.IsNullOrWhiteSpace(vid))
                return Results.BadRequest(new { error = "Vault identity (.notes-vault.json id) is required." });

            var result = store.Create(new IntegrationTokenCreateRequest
            {
                Name = body.Name ?? "",
                Scopes = body.Scopes ?? IntegrationScopes.PresetReadOnly,
                AllowedFolderRoots = body.AllowedFolderRoots ?? [],
                WholeVault = body.WholeVault,
                ExpirationDays = body.NeverExpires ? 0 : body.ExpirationDays ?? 90,
                NeverExpires = body.NeverExpires,
                AdminPassword = body.AdminPassword
            }, vid, time);

            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            // Enabling is separate; creating a token does not auto-enable the feature.
            return Results.Json(new
            {
                success = true,
                token = result.Token,
                plaintextSecret = result.PlaintextSecret,
                warning = "Copy the secret now. It will not be shown again."
            });
        });

        admin.MapPost("/tokens/{id}/revoke", async (string id, HttpContext ctx, IAntiforgery af, IIntegrationTokenStore store, TimeProvider time) =>
        {
            await af.ValidateRequestAsync(ctx);
            return store.Revoke(id, time)
                ? Results.Json(new { success = true })
                : Results.NotFound(new { error = "Token not found" });
        });

        admin.MapPost("/tokens/revoke-all", async (HttpContext ctx, IAntiforgery af, IIntegrationTokenStore store, TimeProvider time) =>
        {
            await af.ValidateRequestAsync(ctx);
            var n = store.RevokeAll(time);
            return Results.Json(new { success = true, revoked = n });
        });

        admin.MapPost("/tokens/{id}/rotate", async (string id, HttpContext ctx, IAntiforgery af, IIntegrationTokenStore store,
            IVaultIdentity vaultId, ILocalAuthService auth, TimeProvider time, HttpRequest req) =>
        {
            await af.ValidateRequestAsync(ctx);
            var body = await req.ReadFromJsonAsync<CreateTokenBody>();
            if (body is null) return Results.BadRequest(new { error = "Invalid body" });
            var reauth = auth.VerifyAdminReauth(body.AdminPassword ?? "", body.TotpCode);
            if (!reauth.Success)
                return Results.Json(new { error = reauth.Error ?? "Administrator reauthentication required", code = "reauth_required", requiresTotp = reauth.RequiresTotp }, statusCode: 403);
            var vid = vaultId.GetVaultId();
            if (string.IsNullOrWhiteSpace(vid))
                return Results.BadRequest(new { error = "Vault identity required" });
            var existing = store.Get(id);
            if (existing is null) return Results.NotFound(new { error = "Token not found" });
            var never = body.NeverExpires || IntegrationTokenExpiry.IsNever(existing.ExpiresAt);
            var result = store.Rotate(id, new IntegrationTokenCreateRequest
            {
                Name = body.Name ?? existing.Name,
                Scopes = body.Scopes ?? existing.Scopes,
                AllowedFolderRoots = body.AllowedFolderRoots ?? existing.AllowedFolderRoots,
                WholeVault = body.WholeVault || existing.WholeVault,
                ExpirationDays = never ? 0 : body.ExpirationDays ?? 90,
                NeverExpires = never
            }, vid, time);
            if (!result.Success) return Results.BadRequest(new { error = result.Error });
            return Results.Json(new { success = true, token = result.Token, plaintextSecret = result.PlaintextSecret });
        });
    }

    private static void MapExternal(WebApplication app)
    {
        var api = app.MapGroup("/api/integrations/v1")
            .RequireAuthorization(IntegrationAuth.Policy)
            .AddEndpointFilter(async (ctx, next) =>
            {
                ctx.HttpContext.Response.Headers.CacheControl = "no-store";
                if (!ctx.HttpContext.RequestServices.GetRequiredService<IIntegrationConfigService>().IsFeatureEnabled())
                    return Results.Json(new { error = "Integrations are disabled", code = "disabled" }, statusCode: 503);
                return await next(ctx);
            });

        var reads = api.MapGroup("").RequireRateLimiting(ReadRatePolicy);
        var writes = api.MapGroup("").RequireRateLimiting(WriteRatePolicy)
            .AddEndpointFilter(async (ctx, next) =>
            {
                var cfg = ctx.HttpContext.RequestServices.GetRequiredService<IIntegrationConfigService>().Get();
                long max = cfg.MaxBodyBytes;
                var ct = ctx.HttpContext.Request.ContentType ?? "";
                if (ct.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                {
                    var jot = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<JotdexOptions>>().Value;
                    max = jot.MaxAttachmentBytes > 0 ? jot.MaxAttachmentBytes : max;
                }
                var len = ctx.HttpContext.Request.ContentLength;
                if (len is > 0 && len > max)
                    return Results.Json(new { error = "Body too large", code = "body_too_large" }, statusCode: 413);
                return await next(ctx);
            });

        reads.MapGet("/whoami", (HttpContext ctx) =>
        {
            var p = ctx.User;
            var expRaw = p.FindFirstValue(IntegrationAuth.ClaimExpiresAt);
            DateTimeOffset? expiresAt = null;
            var neverExpires = false;
            if (DateTimeOffset.TryParse(expRaw, out var exp))
            {
                neverExpires = IntegrationTokenExpiry.IsNever(exp);
                expiresAt = neverExpires ? null : exp;
            }
            return Results.Json(new
            {
                name = p.FindFirstValue(IntegrationAuth.ClaimTokenName),
                permissions = Scopes(p),
                allowedFolders = Folders(p),
                wholeVault = p.FindFirstValue(IntegrationAuth.ClaimWholeVault) == "1",
                apiVersion = "v1",
                expiresAt,
                neverExpires
            });
        });

        reads.MapGet("/folders", (HttpContext ctx, IVaultService vault, IVaultPathGuard paths, IIntegrationFolderAccess acl) =>
        {
            if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesRead)) return ForbiddenScope();
            var folders = acl.FilterFolders(token, FlattenFolders(vault.GetTree()));
            return Results.Json(new { folders });
        });

        reads.MapGet("/notes", (HttpContext ctx, IVaultService vault, IVaultPathGuard paths, IIntegrationFolderAccess acl,
            IIntegrationConfigService cfg, string? folder, int? limit, string? cursor) =>
        {
            if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesRead)) return ForbiddenScope();
            var max = Math.Clamp(limit ?? 50, 1, cfg.Get().MaxPageSize);
            if (folder is not null && !acl.AllowsFolder(token, folder))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var all = vault.ListNotes(folder)
                .Where(n => acl.AllowsNotePath(token, n.RelativePath))
                .OrderBy(n => n.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var start = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var c))
                start = Math.Clamp(c, 0, all.Count);
            var page = all.Skip(start).Take(max).Select(ToSummaryDto).ToList();
            var next = start + page.Count < all.Count ? (start + page.Count).ToString() : null;
            return Results.Json(new { notes = page, nextCursor = next });
        });

        reads.MapGet("/search", (HttpContext ctx, ISearchIndex search, IVaultService vault, IVaultPathGuard paths,
            IIntegrationFolderAccess acl, IIntegrationConfigService cfg, string? q, string? folder, int? limit, bool? includeSnippets) =>
        {
            if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesRead)) return ForbiddenScope();
            if (string.IsNullOrWhiteSpace(q) || q.Length > 500)
                return Results.BadRequest(new { error = "Query required (max 500 chars)", code = "invalid_query" });
            if (folder is not null && !acl.AllowsFolder(token, folder))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var max = Math.Clamp(limit ?? 25, 1, cfg.Get().MaxPageSize);
            var withSnippets = includeSnippets != false;
            // Oversample then filter so scoped results are not starved by unrestricted top-N.
            var raw = search.Search(new SearchRequest { RawQuery = q, Limit = Math.Min(max * 8, 400) });
            var hits = new List<object>();
            foreach (var h in raw.Hits)
            {
                if (folder is not null &&
                    !IntegrationFolderAccess.Normalize(h.FolderPath)
                        .StartsWith(IntegrationFolderAccess.Normalize(folder), StringComparison.OrdinalIgnoreCase) &&
                    !IntegrationFolderAccess.Normalize(h.FolderPath)
                        .Equals(IntegrationFolderAccess.Normalize(folder), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!acl.AllowsNotePath(token, h.RelativePath)) continue;
                object[]? snippets = null;
                if (withSnippets)
                {
                    var note = vault.GetNote(h.NoteId);
                    if (note is not null)
                    {
                        var body = FrontMatterParser.Parse(note.Markdown).Body;
                        snippets = IntegrationMarkdownInsert.FindSnippets(body, q).Cast<object>().ToArray();
                    }
                    else
                        snippets = [];
                }
                hits.Add(new
                {
                    id = h.NoteId,
                    title = h.Title,
                    relativePath = h.RelativePath,
                    folderPath = h.FolderPath,
                    snippet = Truncate(h.Snippet, 280),
                    snippets
                });
                if (hits.Count >= max) break;
            }
            return Results.Json(new { hits, query = q });
        });

        // Must be registered before /notes/{id:guid}
        reads.MapGet("/notes/changes", (HttpContext ctx, IVaultService vault, IVaultPathGuard paths,
            IIntegrationFolderAccess acl, IIntegrationConfigService cfg, string? since, int? limit, string? cursor) =>
        {
            if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesRead)) return ForbiddenScope();
            if (string.IsNullOrWhiteSpace(since) || !DateTimeOffset.TryParse(since, out var sinceAt))
                return Results.BadRequest(new { error = "since required (ISO timestamp)", code = "invalid_since" });

            var max = Math.Clamp(limit ?? 50, 1, cfg.Get().MaxPageSize);
            var all = vault.ListNotes(null)
                .Where(n => acl.AllowsNotePath(token, n.RelativePath))
                .Select(n =>
                {
                    var created = n.Created;
                    var updated = n.Modified ?? n.Created;
                    var changeAt = updated ?? created ?? DateTimeOffset.MinValue;
                    string changeType;
                    if (created is not null && created >= sinceAt)
                        changeType = "created";
                    else if (updated is not null && updated >= sinceAt)
                        changeType = "updated";
                    else
                        changeType = "";
                    return new { note = n, changeAt, changeType };
                })
                .Where(x => x.changeType.Length > 0 && x.changeAt >= sinceAt)
                .OrderByDescending(x => x.changeAt)
                .ThenBy(x => x.note.Id)
                .ToList();

            var start = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var c))
                start = Math.Clamp(c, 0, all.Count);
            var page = all.Skip(start).Take(max).ToList();
            var next = start + page.Count < all.Count ? (start + page.Count).ToString() : null;
            var nextSince = page.Count > 0
                ? page.Min(x => x.changeAt).ToString("O")
                : sinceAt.ToString("O");

            // Resolve etags from detail cache (lightweight for page only).
            var changes = page.Select(x =>
            {
                var detail = vault.GetNote(x.note.Id);
                return new
                {
                    id = x.note.Id,
                    title = x.note.Title,
                    folderPath = x.note.FolderPath,
                    updatedAt = x.changeAt,
                    etag = detail?.ETag,
                    changeType = x.changeType
                };
            }).ToList();

            return Results.Json(new
            {
                changes,
                deleted = Array.Empty<string>(),
                nextCursor = next,
                nextSince
            });
        });

        reads.MapGet("/tasks", (HttpContext ctx, IVaultTaskService tasks, IVaultPathGuard paths,
            IIntegrationFolderAccess acl, IIntegrationConfigService cfg,
            string? status, string? folder, string? folderId, string? updatedSince, int? limit, string? cursor) =>
        {
            if (!paths.IsConfigured) return Results.NotFound(new { error = "Vault not configured" });
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.TasksRead)) return ForbiddenScope();

            var folderFilter = folder ?? folderId;
            if (folderFilter is not null && !acl.AllowsFolder(token, folderFilter))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            DateTimeOffset? sinceAt = null;
            if (!string.IsNullOrWhiteSpace(updatedSince))
            {
                if (!DateTimeOffset.TryParse(updatedSince, out var parsed))
                    return Results.BadRequest(new { error = "updatedSince must be ISO timestamp", code = "invalid_updated_since" });
                sinceAt = parsed;
            }

            var max = Math.Clamp(limit ?? 50, 1, cfg.Get().MaxPageSize);
            var all = tasks.ListTasks(status ?? "open")
                .Where(t =>
                {
                    if (t.StandaloneTodosMd)
                        return token.WholeVault;
                    if (!acl.AllowsNotePath(token, t.NoteRelativePath))
                        return false;
                    if (folderFilter is not null)
                    {
                        var f = IntegrationFolderAccess.Normalize(t.FolderPath);
                        var want = IntegrationFolderAccess.Normalize(folderFilter);
                        if (!f.Equals(want, StringComparison.OrdinalIgnoreCase) &&
                            !f.StartsWith(want + "/", StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    if (sinceAt is not null)
                    {
                        var u = t.NoteModified ?? t.NoteCreated;
                        if (u is null || u < sinceAt) return false;
                    }
                    return true;
                })
                .ToList();

            var start = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var c))
                start = Math.Clamp(c, 0, all.Count);
            var page = all.Skip(start).Take(max).Select(ToTaskDto).ToList();
            var next = start + page.Count < all.Count ? (start + page.Count).ToString() : null;
            return Results.Json(new { tasks = page, nextCursor = next });
        });

        writes.MapPost("/tasks", async (HttpContext ctx, IVaultTaskService tasks, IIdempotencyStore idem,
            IIntegrationActivityLog log, TimeProvider time, HttpRequest req) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.TasksWrite)) return ForbiddenScope();
            if (!token.WholeVault)
                return Results.Json(new { error = "Standalone todo-list writes require whole-vault access", code = "whole_vault_required" }, statusCode: 403);
            if (!req.Headers.TryGetValue("Idempotency-Key", out var idk) || string.IsNullOrWhiteSpace(idk))
                return Results.BadRequest(new { error = "Idempotency-Key required", code = "idempotency_required" });

            var body = await req.ReadFromJsonAsync<CreateTaskBody>();
            if (body is null || string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text required", code = "invalid_body" });

            var fingerprint = Fingerprint("POST", "/tasks", body.Text + "\n" + (body.DueDate ?? ""));
            var ns = IdemNs(token, "POST", "/tasks");
            var prior = idem.TryGet(ns, idk!, fingerprint);
            if (prior.FingerprintMismatch)
                return Results.Conflict(new { error = "Idempotency-Key reused with different body", code = "idempotency_conflict" });
            if (prior.Found && prior.ResponseJson is not null)
                return Results.Content(prior.ResponseJson, "application/json", statusCode: prior.StatusCode);

            var change = new NoteChangeContext(NoteChangeVia.Api, token.Name, token.Id);
            var result = tasks.CreateStandalone(body.Text.Trim(), body.DueDate, change);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error, code = "create_failed" });

            var dto = result.Task is null ? null : ToTaskDto(result.Task);
            var json = JsonSerializer.Serialize(new { task = dto, etag = result.ETag }, JsonOpts);
            idem.Put(ns, idk!, fingerprint, 200, json, time);
            log.Record(new IntegrationActivityEntry
            {
                At = time.GetUtcNow(),
                TokenId = token.Id,
                TokenName = token.Name,
                Operation = "tasks.create",
                NoteId = result.NoteId?.ToString("D"),
                Outcome = "ok",
                RequestId = ctx.TraceIdentifier,
                EtagAfter = result.ETag
            });
            return Results.Content(json, "application/json");
        });

        writes.MapPatch("/tasks/{taskId}", async (string taskId, HttpContext ctx, IVaultTaskService tasks,
            IIntegrationFolderAccess acl, IIntegrationActivityLog log, TimeProvider time, HttpRequest req) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.TasksWrite)) return ForbiddenScope();
            if (!req.Headers.TryGetValue("If-Match", out var match) || string.IsNullOrWhiteSpace(match) || match == "*")
                return Results.Json(new { error = "If-Match required (exact ETag)", code = "precondition_required" }, statusCode: 428);

            var existing = tasks.ListTasks("all").FirstOrDefault(t =>
                t.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                return Results.NotFound(new { error = "Not found", code = "not_found" });
            if (existing.StandaloneTodosMd)
            {
                if (!token.WholeVault)
                    return Results.Json(new { error = "Standalone todo-list access requires whole-vault", code = "whole_vault_required" }, statusCode: 403);
            }
            else if (!acl.AllowsNotePath(token, existing.NoteRelativePath))
            {
                return Results.NotFound(new { error = "Not found", code = "not_found" });
            }

            var body = await req.ReadFromJsonAsync<PatchTaskBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body", code = "invalid_body" });

            var etag = match.ToString().Trim().Trim('"');
            var change = new NoteChangeContext(NoteChangeVia.Api, token.Name, token.Id);
            var result = tasks.Patch(taskId, new VaultTaskPatch
            {
                Text = body.Text,
                Done = body.Done,
                Due = body.DueDate
            }, etag, change);
            if (result.Conflict)
                return Results.Json(new { error = "Stale ETag — reread before retry", code = "precondition_failed", etag = result.ETag }, statusCode: 412);
            if (!result.Success)
                return result.Error == "Task not found"
                    ? Results.NotFound(new { error = "Not found", code = "not_found" })
                    : Results.BadRequest(new { error = result.Error, code = "patch_failed" });

            log.Record(new IntegrationActivityEntry
            {
                At = time.GetUtcNow(),
                TokenId = token.Id,
                TokenName = token.Name,
                Operation = "tasks.patch",
                NoteId = result.NoteId?.ToString("D"),
                Outcome = "ok",
                RequestId = ctx.TraceIdentifier,
                EtagBefore = etag,
                EtagAfter = result.ETag
            });
            return Results.Json(new { task = result.Task is null ? null : ToTaskDto(result.Task), etag = result.ETag });
        });

        reads.MapGet("/notes/{id:guid}/export", (Guid id, HttpContext ctx, IVaultService vault, IIntegrationFolderAccess acl,
            INoteShareExportService share, string? format, string? theme, bool? includeTitle) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesRead)) return ForbiddenScope();
            var note = vault.GetNote(id);
            if (note is null || !acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var fmt = (format ?? "html").Trim().ToLowerInvariant();
            if (fmt == "pdf")
                return Results.Json(new { error = "PDF export is not available", code = "unsupported_format" }, statusCode: 400);
            if (fmt is not ("html" or "md"))
                return Results.BadRequest(new { error = "format must be html or md", code = "invalid_format" });

            ctx.Response.Headers["X-Jotdex-Etag"] = note.ETag;

            if (fmt == "md")
            {
                var body = FrontMatterParser.Parse(note.Markdown).Body;
                var mdBytes = Encoding.UTF8.GetBytes(body);
                var mdName = SanitizeExportFileName(note.Title, note.Id) + ".md";
                return Results.File(mdBytes, "text/markdown; charset=utf-8", mdName);
            }

            var th = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
            var withTitle = includeTitle != false;
            var result = share.ExportSelfContainedHtml(id, new NoteShareExportOptions
            {
                Theme = th,
                IncludeTitle = withTitle
            });
            if (!result.Success || string.IsNullOrEmpty(result.Html) || string.IsNullOrEmpty(result.FileName))
                return Results.BadRequest(new { error = result.Error ?? "Export failed", code = "export_failed" });

            var htmlBytes = Encoding.UTF8.GetBytes(result.Html);
            return Results.File(htmlBytes, "text/html; charset=utf-8", result.FileName);
        });

        reads.MapGet("/notes/{id:guid}", (Guid id, HttpContext ctx, IVaultService vault, IIntegrationFolderAccess acl, IIntegrationActivityLog log) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesRead)) return ForbiddenScope();
            var note = vault.GetNote(id);
            if (note is null || !acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });
            log.Record(new IntegrationActivityEntry
            {
                At = DateTimeOffset.UtcNow,
                TokenId = token.Id,
                TokenName = token.Name,
                Operation = "notes.read",
                NoteId = id.ToString("D"),
                Outcome = "ok",
                RequestId = ctx.TraceIdentifier
            });
            return Results.Json(ToNoteDto(note));
        });

        writes.MapPost("/notes", async (HttpContext ctx, INoteCommandService commands, IVaultPathGuard paths,
            IIntegrationFolderAccess acl, IIdempotencyStore idem, IIntegrationActivityLog log, TimeProvider time, HttpRequest req) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesCreate)) return ForbiddenScope();
            if (!req.Headers.TryGetValue("Idempotency-Key", out var idk) || string.IsNullOrWhiteSpace(idk))
                return Results.BadRequest(new { error = "Idempotency-Key required", code = "idempotency_required" });

            var body = await req.ReadFromJsonAsync<CreateNoteBody>();
            if (body is null || string.IsNullOrWhiteSpace(body.Title) || string.IsNullOrWhiteSpace(body.FolderPath))
                return Results.BadRequest(new { error = "title and folderPath required", code = "invalid_body" });
            if (!acl.AllowsFolder(token, body.FolderPath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var fingerprint = Fingerprint("POST", "/notes", body.Title + "\n" + body.FolderPath + "\n" + (body.BodyMarkdown ?? ""));
            var ns = IdemNs(token, "POST", "/notes");
            var prior = idem.TryGet(ns, idk!, fingerprint);
            if (prior.FingerprintMismatch)
                return Results.Conflict(new { error = "Idempotency-Key reused with different body", code = "idempotency_conflict" });
            if (prior.Found && prior.ResponseJson is not null)
                return Results.Content(prior.ResponseJson, "application/json", statusCode: prior.StatusCode);

            var change = new NoteChangeContext(NoteChangeVia.Api, token.Name, token.Id);
            var created = commands.Create(body.FolderPath, body.Title.Trim(), body.BodyMarkdown, change);
            if (created is null)
                return Results.BadRequest(new { error = "Create failed", code = "create_failed" });

            var dto = ToNoteDto(created);
            var json = JsonSerializer.Serialize(dto, JsonOpts);
            idem.Put(ns, idk!, fingerprint, 200, json, time);
            log.Record(new IntegrationActivityEntry
            {
                At = time.GetUtcNow(),
                TokenId = token.Id,
                TokenName = token.Name,
                Operation = "notes.create",
                NoteId = created.Id.ToString("D"),
                Outcome = "ok",
                RequestId = ctx.TraceIdentifier,
                EtagAfter = created.ETag
            });
            return Results.Content(json, "application/json");
        });

        writes.MapPut("/notes/{id:guid}", async (Guid id, HttpContext ctx, INoteCommandService commands, IVaultService vault,
            IIntegrationFolderAccess acl, IIntegrationActivityLog log, TimeProvider time, HttpRequest req) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesUpdate)) return ForbiddenScope();
            if (!req.Headers.TryGetValue("If-Match", out var match) || string.IsNullOrWhiteSpace(match) || match == "*")
                return Results.Json(new { error = "If-Match required (exact ETag)", code = "precondition_required" }, statusCode: 428);

            var note = vault.GetNote(id);
            if (note is null || !acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var body = await req.ReadFromJsonAsync<BodyOnly>();
            if (body?.BodyMarkdown is null)
                return Results.BadRequest(new { error = "bodyMarkdown required", code = "invalid_body" });

            // Recheck path inside mutation boundary via ReplaceBody → SaveCore disk read
            if (!acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var etag = match.ToString().Trim().Trim('"');
            var change = new NoteChangeContext(NoteChangeVia.Api, token.Name, token.Id);
            var result = commands.ReplaceBody(id, body.BodyMarkdown, etag, change);
            if (result.Conflict)
                return Results.Json(new { error = "Stale ETag — reread before retry", code = "precondition_failed", etag = result.ETag }, statusCode: 412);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error, code = "update_failed" });

            log.Record(new IntegrationActivityEntry
            {
                At = time.GetUtcNow(),
                TokenId = token.Id,
                TokenName = token.Name,
                Operation = "notes.update",
                NoteId = id.ToString("D"),
                Outcome = "ok",
                RequestId = ctx.TraceIdentifier,
                EtagBefore = etag,
                EtagAfter = result.ETag
            });
            return Results.Json(ToNoteDto(result.Note!));
        });

        writes.MapPost("/notes/{id:guid}/append", async (Guid id, HttpContext ctx, INoteCommandService commands, IVaultService vault,
            IIntegrationFolderAccess acl, IIdempotencyStore idem, IIntegrationActivityLog log, TimeProvider time, HttpRequest req) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesAppend)) return ForbiddenScope();
            if (!req.Headers.TryGetValue("Idempotency-Key", out var idk) || string.IsNullOrWhiteSpace(idk))
                return Results.BadRequest(new { error = "Idempotency-Key required", code = "idempotency_required" });
            if (!req.Headers.TryGetValue("If-Match", out var match) || string.IsNullOrWhiteSpace(match) || match == "*")
                return Results.Json(new { error = "If-Match required (exact ETag)", code = "precondition_required" }, statusCode: 428);

            var note = vault.GetNote(id);
            if (note is null || !acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var body = await req.ReadFromJsonAsync<BodyOnly>();
            if (body?.BodyMarkdown is null)
                return Results.BadRequest(new { error = "bodyMarkdown required", code = "invalid_body" });

            var etag = match.ToString().Trim().Trim('"');
            var fingerprint = Fingerprint("POST", $"/notes/{id}/append", etag + "\n" + body.BodyMarkdown);
            var ns = IdemNs(token, "POST", $"/notes/{id}/append");
            var prior = idem.TryGet(ns, idk!, fingerprint);
            if (prior.FingerprintMismatch)
                return Results.Conflict(new { error = "Idempotency-Key reused with different body", code = "idempotency_conflict" });
            if (prior.Found && prior.ResponseJson is not null)
                return Results.Content(prior.ResponseJson, "application/json", statusCode: prior.StatusCode);

            var change = new NoteChangeContext(NoteChangeVia.Api, token.Name, token.Id);
            var result = commands.AppendBody(id, body.BodyMarkdown, etag, change);
            if (result.Conflict)
                return Results.Json(new { error = "Stale ETag — reread before retry", code = "precondition_failed", etag = result.ETag }, statusCode: 412);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error, code = "append_failed" });

            var json = JsonSerializer.Serialize(ToNoteDto(result.Note!), JsonOpts);
            idem.Put(ns, idk!, fingerprint, 200, json, time);
            log.Record(new IntegrationActivityEntry
            {
                At = time.GetUtcNow(),
                TokenId = token.Id,
                TokenName = token.Name,
                Operation = "notes.append",
                NoteId = id.ToString("D"),
                Outcome = "ok",
                RequestId = ctx.TraceIdentifier,
                EtagBefore = etag,
                EtagAfter = result.ETag
            });
            return Results.Content(json, "application/json");
        });

        writes.MapPost("/notes/{id:guid}/insert", async (Guid id, HttpContext ctx, INoteCommandService commands, IVaultService vault,
            IIntegrationFolderAccess acl, IIdempotencyStore idem, IIntegrationActivityLog log, TimeProvider time, HttpRequest req) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.NotesInsert) && !HasScope(ctx, IntegrationScopes.NotesAppend))
                return ForbiddenScope();
            if (!req.Headers.TryGetValue("Idempotency-Key", out var idk) || string.IsNullOrWhiteSpace(idk))
                return Results.BadRequest(new { error = "Idempotency-Key required", code = "idempotency_required" });
            if (!req.Headers.TryGetValue("If-Match", out var match) || string.IsNullOrWhiteSpace(match) || match == "*")
                return Results.Json(new { error = "If-Match required (exact ETag)", code = "precondition_required" }, statusCode: 428);

            var note = vault.GetNote(id);
            if (note is null || !acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var body = await req.ReadFromJsonAsync<InsertBody>();
            if (body is null || string.IsNullOrWhiteSpace(body.Markdown))
                return Results.BadRequest(new { error = "markdown required", code = "invalid_body" });
            var position = (body.Position ?? "").Trim();
            if (position is not ("top" or "afterHeading"))
                return Results.BadRequest(new { error = "position must be top or afterHeading", code = "invalid_position" });
            if (position == "afterHeading" && string.IsNullOrWhiteSpace(body.Heading))
                return Results.BadRequest(new { error = "heading required for afterHeading", code = "invalid_heading" });

            var etag = match.ToString().Trim().Trim('"');
            var occurrence = body.Occurrence is > 0 ? body.Occurrence.Value : 1;
            var fingerprint = Fingerprint("POST", $"/notes/{id}/insert", position + "\n" + (body.Heading ?? "") + "\n" + occurrence + "\n" + etag + "\n" + body.Markdown);
            var ns = IdemNs(token, "POST", $"/notes/{id}/insert");
            var prior = idem.TryGet(ns, idk!, fingerprint);
            if (prior.FingerprintMismatch)
                return Results.Conflict(new { error = "Idempotency-Key reused with different body", code = "idempotency_conflict" });
            if (prior.Found && prior.ResponseJson is not null)
                return Results.Content(prior.ResponseJson, "application/json", statusCode: prior.StatusCode);

            if (!acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var change = new NoteChangeContext(NoteChangeVia.Api, token.Name, token.Id);
            var result = commands.InsertMarkdown(id, body.Markdown, position, body.Heading, occurrence, etag, change);
            if (result.Error == "Heading not found")
                return Results.Json(new { error = "Heading not found", code = "heading_not_found" }, statusCode: 404);
            if (result.Conflict)
                return Results.Json(new { error = "Stale ETag — reread before retry", code = "precondition_failed", etag = result.ETag }, statusCode: 412);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error, code = "insert_failed" });

            var json = JsonSerializer.Serialize(ToNoteDto(result.Note!), JsonOpts);
            idem.Put(ns, idk!, fingerprint, 200, json, time);
            log.Record(new IntegrationActivityEntry
            {
                At = time.GetUtcNow(),
                TokenId = token.Id,
                TokenName = token.Name,
                Operation = "notes.insert",
                NoteId = id.ToString("D"),
                Outcome = "ok",
                RequestId = ctx.TraceIdentifier,
                EtagBefore = etag,
                EtagAfter = result.ETag
            });
            return Results.Content(json, "application/json");
        });

        reads.MapGet("/notes/{id:guid}/attachments", (Guid id, HttpContext ctx, IVaultService vault,
            IVaultPathGuard paths, IIntegrationFolderAccess acl) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.AttachmentsRead)) return ForbiddenScope();
            var note = vault.GetNote(id);
            if (note is null || !acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var list = note.Attachments.Select(a =>
            {
                DateTimeOffset? createdAt = null;
                try
                {
                    var abs = paths.EnsureInsideVault(a.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(abs))
                        createdAt = File.GetCreationTimeUtc(abs);
                }
                catch { /* ignore */ }
                return new
                {
                    attachmentId = a.Id,
                    filename = a.FileName,
                    contentType = a.ContentType,
                    size = a.SizeBytes,
                    createdAt
                };
            }).ToList();
            return Results.Json(new { attachments = list });
        });

        reads.MapGet("/notes/{id:guid}/attachments/{attachmentId}", (Guid id, string attachmentId, HttpContext ctx,
            IVaultService vault, IIntegrationFolderAccess acl) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.AttachmentsRead)) return ForbiddenScope();
            var note = vault.GetNote(id);
            if (note is null || !acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });
            var att = note.Attachments.FirstOrDefault(a => a.Id.Equals(attachmentId, StringComparison.OrdinalIgnoreCase));
            if (att is null) return Results.NotFound(new { error = "Not found", code = "not_found" });
            try
            {
                var stream = vault.OpenAttachmentStream(att.Id);
                return Results.File(stream, att.ContentType, att.FileName);
            }
            catch
            {
                return Results.NotFound(new { error = "Not found", code = "not_found" });
            }
        });

        writes.MapPost("/notes/{id:guid}/attachments", async (Guid id, HttpContext ctx, INoteCommandService commands,
            IVaultService vault, IVaultPathGuard paths, IIntegrationFolderAccess acl, IIdempotencyStore idem,
            IIntegrationActivityLog log, IOptions<JotdexOptions> jotOpts, TimeProvider time, HttpRequest req) =>
        {
            var token = RequireToken(ctx);
            if (token is null) return Results.Unauthorized();
            if (!HasScope(ctx, IntegrationScopes.AttachmentsWrite)) return ForbiddenScope();
            if (!req.Headers.TryGetValue("Idempotency-Key", out var idk) || string.IsNullOrWhiteSpace(idk))
                return Results.BadRequest(new { error = "Idempotency-Key required", code = "idempotency_required" });
            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required", code = "invalid_content_type" });

            var note = vault.GetNote(id);
            if (note is null || !acl.AllowsNotePath(token, note.RelativePath))
                return Results.NotFound(new { error = "Not found", code = "not_found" });

            var feature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
                feature.MaxRequestBodySize = jotOpts.Value.MaxAttachmentBytes;

            var form = await req.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file required", code = "invalid_body" });
            if (file.Length > jotOpts.Value.MaxAttachmentBytes)
                return Results.Json(new { error = $"File exceeds max size ({jotOpts.Value.MaxAttachmentBytes} bytes)", code = "file_too_large" }, statusCode: 413);

            var filenameOverride = form["filename"].ToString();
            var altText = form["altText"].ToString();
            var placement = (form["placement"].ToString() ?? "none").Trim();
            if (string.IsNullOrEmpty(placement)) placement = "none";
            var heading = form["heading"].ToString();
            if (placement is not ("none" or "append" or "top" or "afterHeading"))
                return Results.BadRequest(new { error = "placement must be none, append, top, or afterHeading", code = "invalid_placement" });
            if (placement == "afterHeading" && string.IsNullOrWhiteSpace(heading))
                return Results.BadRequest(new { error = "heading required for afterHeading", code = "invalid_heading" });

            string? ifMatch = null;
            if (req.Headers.TryGetValue("If-Match", out var matchHdr) && !string.IsNullOrWhiteSpace(matchHdr))
            {
                if (matchHdr == "*")
                    return Results.Json(new { error = "If-Match required (exact ETag)", code = "precondition_required" }, statusCode: 428);
                ifMatch = matchHdr.ToString().Trim().Trim('"');
            }
            if (placement != "none" && string.IsNullOrEmpty(ifMatch))
                return Results.Json(new { error = "If-Match required when placement is set", code = "precondition_required" }, statusCode: 428);

            await using var uploadStream = file.OpenReadStream();
            using var ms = new MemoryStream();
            await uploadStream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var header = bytes.AsSpan(0, Math.Min(bytes.Length, 64));
            var declaredName = string.IsNullOrWhiteSpace(filenameOverride) ? file.FileName : filenameOverride.Trim();
            if (!IntegrationAttachmentValidation.TryDetect(header, declaredName, file.ContentType, out var detectedCt, out var detectErr))
                return Results.BadRequest(new { error = detectErr, code = "invalid_file_type" });

            var fileHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var fingerprint = Fingerprint("POST", $"/notes/{id}/attachments",
                fileHash + "\n" + declaredName + "\n" + altText + "\n" + placement + "\n" + heading + "\n" + (ifMatch ?? ""));
            var ns = IdemNs(token, "POST", $"/notes/{id}/attachments");
            var prior = idem.TryGet(ns, idk!, fingerprint);
            if (prior.FingerprintMismatch)
                return Results.Conflict(new { error = "Idempotency-Key reused with different body", code = "idempotency_conflict" });
            if (prior.Found && prior.ResponseJson is not null)
                return Results.Content(prior.ResponseJson, "application/json", statusCode: prior.StatusCode);

            if (placement != "none" && ifMatch is not null &&
                !string.Equals(note.ETag.Trim('"'), ifMatch, StringComparison.Ordinal))
                return Results.Json(new { error = "Stale ETag — reread before retry", code = "precondition_failed", etag = note.ETag }, statusCode: 412);

            ms.Position = 0;
            var uploaded = commands.AddAttachment(id, ms, declaredName, detectedCt);
            if (!uploaded.Success)
                return Results.BadRequest(new { error = uploaded.Error, code = "upload_failed" });

            var isImage = uploaded.IsImage;
            var label = string.IsNullOrWhiteSpace(altText)
                ? (isImage ? Path.GetFileNameWithoutExtension(uploaded.FileName) ?? "image" : uploaded.FileName ?? "file")
                : altText.Trim();
            var markdown = IntegrationAttachmentValidation.BuildMarkdownSnippet(isImage, label!, uploaded.MarkdownPath!);

            NoteDetail? placedNote = uploaded.Note;
            string? etagAfter = uploaded.Note?.ETag;
            if (placement != "none")
            {
                var change = new NoteChangeContext(NoteChangeVia.Api, token.Name, token.Id);
                NoteSaveResult placeResult;
                if (placement == "append")
                    placeResult = commands.AppendBody(id, markdown, ifMatch!, change);
                else
                    placeResult = commands.InsertMarkdown(id, markdown, placement, heading, 1, ifMatch!, change);

                if (placeResult.Error == "Heading not found")
                    return Results.Json(new { error = "Heading not found", code = "heading_not_found" }, statusCode: 404);
                if (placeResult.Conflict)
                    return Results.Json(new { error = "Stale ETag — reread before retry", code = "precondition_failed", etag = placeResult.ETag }, statusCode: 412);
                if (!placeResult.Success)
                    return Results.BadRequest(new { error = placeResult.Error, code = "placement_failed" });
                placedNote = placeResult.Note;
                etagAfter = placeResult.ETag;
            }

            var size = placedNote?.Attachments.FirstOrDefault(a => a.Id == uploaded.AttachmentId)?.SizeBytes
                       ?? bytes.LongLength;
            var payload = new
            {
                attachmentId = uploaded.AttachmentId,
                filename = uploaded.FileName,
                contentType = uploaded.ContentType ?? detectedCt,
                size,
                markdown,
                etag = etagAfter,
                placement
            };
            var json = JsonSerializer.Serialize(payload, JsonOpts);
            idem.Put(ns, idk!, fingerprint, 200, json, time);
            log.Record(new IntegrationActivityEntry
            {
                At = time.GetUtcNow(),
                TokenId = token.Id,
                TokenName = token.Name,
                Operation = "attachments.upload",
                NoteId = id.ToString("D"),
                Outcome = "ok",
                RequestId = ctx.TraceIdentifier,
                EtagBefore = ifMatch,
                EtagAfter = etagAfter
            });
            return Results.Content(json, "application/json");
        });

        reads.MapGet("/openapi.json", () => Results.Json(OpenApiDocument()));
    }

    private static string SanitizeExportFileName(string title, Guid id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.Trim())
        {
            if (invalid.Contains(ch) || ch < 32) sb.Append('-');
            else sb.Append(ch);
        }
        var name = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim(' ', '.', '-');
        if (string.IsNullOrWhiteSpace(name)) name = id.ToString("N")[..8];
        return name.Length > 80 ? name[..80].Trim() : name;
    }

    private static object ToNoteDto(NoteDetail n)
    {
        var parsed = FrontMatterParser.Parse(n.Markdown);
        return new
        {
            id = n.Id,
            title = n.Title,
            relativePath = n.RelativePath,
            folderPath = n.FolderPath,
            tags = n.Tags,
            bodyMarkdown = parsed.Body,
            etag = n.ETag,
            createdAt = n.Created,
            updatedAt = n.Modified,
            createdVia = n.CreatedVia,
            updatedVia = n.UpdatedVia,
            updatedBy = n.UpdatedBy,
            lastApiUpdateAt = n.LastApiUpdateAt,
            lastApiUpdateBy = n.LastApiUpdateBy,
            provenanceInferred = n.ProvenanceInferred
        };
    }

    private static object ToSummaryDto(NoteSummary n) => new
    {
        id = n.Id,
        title = n.Title,
        relativePath = n.RelativePath,
        folderPath = n.FolderPath,
        tags = n.Tags,
        createdAt = n.Created,
        updatedAt = n.Modified
    };

    private static object ToTaskDto(VaultTaskDto t) => new
    {
        id = t.Id,
        text = t.Text,
        done = t.Done,
        dueDate = t.Due,
        source = t.StandaloneTodosMd ? "todo-list" : "note",
        noteId = t.StandaloneTodosMd ? null : (Guid?)t.NoteId,
        noteTitle = t.StandaloneTodosMd ? null : t.NoteTitle,
        folderPath = t.StandaloneTodosMd ? null : t.FolderPath,
        line = t.LineIndex + 1,
        etag = t.ETag,
        createdAt = t.Added ?? t.NoteCreated?.ToString("O"),
        updatedAt = t.NoteModified
    };

    private static IntegrationTokenRecord? RequireToken(HttpContext ctx)
    {
        var id = ctx.User.FindFirstValue(IntegrationAuth.ClaimTokenId);
        if (id is null) return null;
        return ctx.RequestServices.GetRequiredService<IIntegrationTokenStore>().Get(id);
    }

    private static bool HasScope(HttpContext ctx, string scope) =>
        Scopes(ctx.User).Contains(scope, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Scopes(ClaimsPrincipal p) =>
        (p.FindFirstValue(IntegrationAuth.ClaimScopes) ?? "")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> Folders(ClaimsPrincipal p) =>
        (p.FindFirstValue(IntegrationAuth.ClaimFolders) ?? "")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IResult ForbiddenScope() =>
        Results.Json(new { error = "Insufficient scope", code = "forbidden_scope" }, statusCode: 403);

    private static string IdemNs(IntegrationTokenRecord token, string method, string route) =>
        $"{token.VaultId}|{token.Id}|{method}|{route}";

    private static string Fingerprint(string method, string route, string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(method + "\n" + route + "\n" + body))).ToLowerInvariant();

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    private static object SetupGuidePlaceholder() => new
    {
        headers = new[]
        {
            "CF-Access-Client-Id: <cloudflare-service-client-id>",
            "CF-Access-Client-Secret: <cloudflare-service-client-secret>",
            "Authorization: Bearer <jotdex-api-token>"
        },
        basePath = "/api/integrations/v1",
        docs = "See docs/integrations-api.md"
    };

    private static object OpenApiDocument()
    {
        var note = new
        {
            type = "object",
            properties = new
            {
                id = new { type = "string", format = "uuid" },
                title = new { type = "string" },
                relativePath = new { type = "string" },
                folderPath = new { type = "string" },
                tags = new { type = "array", items = new { type = "string" } },
                bodyMarkdown = new { type = "string" },
                etag = new { type = "string" },
                createdAt = new { type = "string", format = "date-time" },
                updatedAt = new { type = "string", format = "date-time" }
            }
        };
        var task = new
        {
            type = "object",
            properties = new
            {
                id = new { type = "string" },
                text = new { type = "string" },
                done = new { type = "boolean" },
                dueDate = new { type = "string", nullable = true },
                source = new { type = "string", @enum = new[] { "todo-list", "note" } },
                noteId = new { type = "string", format = "uuid", nullable = true },
                noteTitle = new { type = "string", nullable = true },
                folderPath = new { type = "string", nullable = true },
                line = new { type = "integer" },
                etag = new { type = "string" },
                createdAt = new { type = "string", nullable = true },
                updatedAt = new { type = "string", format = "date-time", nullable = true }
            }
        };
        return new
        {
            openapi = "3.0.3",
            info = new { title = "Jotdex Integrations API", version = "v1" },
            paths = new Dictionary<string, object>
            {
                ["/api/integrations/v1/whoami"] = new
                {
                    get = new
                    {
                        summary = "Verify token",
                        responses = new { @default = new { description = "Token identity and scopes" } }
                    }
                },
                ["/api/integrations/v1/folders"] = new { get = new { summary = "List permitted folders", security = Scope("notes:read") } },
                ["/api/integrations/v1/notes"] = new
                {
                    get = new { summary = "List notes", security = Scope("notes:read") },
                    post = new
                    {
                        summary = "Create note",
                        security = Scope("notes:create"),
                        parameters = new object[]
                        {
                            new { name = "Idempotency-Key", @in = "header", required = true, schema = new { type = "string" } }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new
                            {
                                application_json = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "title", "folderPath" },
                                        properties = new
                                        {
                                            title = new { type = "string" },
                                            folderPath = new { type = "string" },
                                            bodyMarkdown = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new { @default = new { description = "Created note", content = new { application_json = new { schema = note } } } }
                    }
                },
                ["/api/integrations/v1/notes/changes"] = new
                {
                    get = new
                    {
                        summary = "Notes changed since timestamp",
                        security = Scope("notes:read"),
                        parameters = new object[]
                        {
                            new { name = "since", @in = "query", required = true, schema = new { type = "string", format = "date-time" } },
                            new { name = "limit", @in = "query", schema = new { type = "integer" } },
                            new { name = "cursor", @in = "query", schema = new { type = "string" } }
                        }
                    }
                },
                ["/api/integrations/v1/search"] = new
                {
                    get = new
                    {
                        summary = "Search notes",
                        security = Scope("notes:read"),
                        parameters = new object[]
                        {
                            new { name = "q", @in = "query", required = true, schema = new { type = "string" } },
                            new { name = "includeSnippets", @in = "query", schema = new { type = "boolean", @default = true } }
                        }
                    }
                },
                ["/api/integrations/v1/notes/{id}"] = new
                {
                    get = new { summary = "Get note", security = Scope("notes:read") },
                    put = new
                    {
                        summary = "Replace body",
                        security = Scope("notes:update"),
                        parameters = new object[]
                        {
                            new { name = "If-Match", @in = "header", required = true, schema = new { type = "string" } }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new
                            {
                                application_json = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "bodyMarkdown" },
                                        properties = new { bodyMarkdown = new { type = "string" } }
                                    }
                                }
                            }
                        }
                    }
                },
                ["/api/integrations/v1/notes/{id}/append"] = new
                {
                    post = new
                    {
                        summary = "Append body",
                        security = Scope("notes:append"),
                        parameters = new object[]
                        {
                            new { name = "Idempotency-Key", @in = "header", required = true, schema = new { type = "string" } },
                            new { name = "If-Match", @in = "header", required = true, schema = new { type = "string" } }
                        }
                    }
                },
                ["/api/integrations/v1/notes/{id}/insert"] = new
                {
                    post = new
                    {
                        summary = "Insert markdown at top or after a heading",
                        security = Scope("notes:insert"),
                        parameters = new object[]
                        {
                            new { name = "Idempotency-Key", @in = "header", required = true, schema = new { type = "string" } },
                            new { name = "If-Match", @in = "header", required = true, schema = new { type = "string" } }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new
                            {
                                application_json = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "markdown", "position" },
                                        properties = new
                                        {
                                            markdown = new { type = "string" },
                                            position = new { type = "string", @enum = new[] { "top", "afterHeading" } },
                                            heading = new { type = "string" },
                                            occurrence = new { type = "integer", @default = 1 }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                ["/api/integrations/v1/notes/{id}/export"] = new
                {
                    get = new
                    {
                        summary = "Export note as Share HTML or raw Markdown body",
                        security = Scope("notes:read"),
                        parameters = new object[]
                        {
                            new { name = "format", @in = "query", schema = new { type = "string", @enum = new[] { "html", "md" }, @default = "html" } },
                            new { name = "theme", @in = "query", schema = new { type = "string", @enum = new[] { "light", "dark" }, @default = "light" } },
                            new { name = "includeTitle", @in = "query", schema = new { type = "boolean", @default = true } }
                        },
                        responses = new
                        {
                            @default = new
                            {
                                description = "HTML or Markdown file; X-Jotdex-Etag header",
                                content = new Dictionary<string, object>
                                {
                                    ["text/html"] = new { schema = new { type = "string", format = "binary" } },
                                    ["text/markdown"] = new { schema = new { type = "string", format = "binary" } }
                                },
                                headers = new
                                {
                                    X_Jotdex_Etag = new { schema = new { type = "string" }, description = "Note etag" }
                                }
                            }
                        }
                    }
                },
                ["/api/integrations/v1/notes/{id}/attachments"] = new
                {
                    get = new
                    {
                        summary = "List note attachments",
                        security = Scope("attachments:read"),
                        responses = new
                        {
                            @default = new
                            {
                                description = "Attachment list",
                                content = new
                                {
                                    application_json = new
                                    {
                                        schema = new
                                        {
                                            type = "object",
                                            properties = new
                                            {
                                                attachments = new
                                                {
                                                    type = "array",
                                                    items = new
                                                    {
                                                        type = "object",
                                                        properties = new
                                                        {
                                                            attachmentId = new { type = "string" },
                                                            filename = new { type = "string" },
                                                            contentType = new { type = "string" },
                                                            size = new { type = "integer" },
                                                            createdAt = new { type = "string", format = "date-time", nullable = true }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    post = new
                    {
                        summary = "Upload attachment into note .assets (optional placement into note body)",
                        security = Scope("attachments:write"),
                        parameters = new object[]
                        {
                            new { name = "Idempotency-Key", @in = "header", required = true, schema = new { type = "string" } },
                            new { name = "If-Match", @in = "header", required = false, schema = new { type = "string" }, description = "Required when placement is not none" }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["multipart/form-data"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "file" },
                                        properties = new
                                        {
                                            file = new { type = "string", format = "binary" },
                                            filename = new { type = "string" },
                                            altText = new { type = "string" },
                                            placement = new { type = "string", @enum = new[] { "none", "append", "top", "afterHeading" }, @default = "none" },
                                            heading = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new
                        {
                            @default = new
                            {
                                description = "Upload result with markdown snippet",
                                content = new
                                {
                                    application_json = new
                                    {
                                        schema = new
                                        {
                                            type = "object",
                                            properties = new
                                            {
                                                attachmentId = new { type = "string" },
                                                filename = new { type = "string" },
                                                contentType = new { type = "string" },
                                                size = new { type = "integer" },
                                                markdown = new { type = "string" },
                                                etag = new { type = "string", nullable = true },
                                                placement = new { type = "string" }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                ["/api/integrations/v1/notes/{id}/attachments/{attachmentId}"] = new
                {
                    get = new
                    {
                        summary = "Download attachment",
                        security = Scope("attachments:read"),
                        responses = new
                        {
                            @default = new
                            {
                                description = "Binary attachment",
                                content = new Dictionary<string, object>
                                {
                                    ["application/octet-stream"] = new { schema = new { type = "string", format = "binary" } }
                                }
                            }
                        }
                    }
                },
                ["/api/integrations/v1/tasks"] = new
                {
                    get = new
                    {
                        summary = "List tasks",
                        security = Scope("tasks:read"),
                        parameters = new object[]
                        {
                            new { name = "status", @in = "query", schema = new { type = "string", @enum = new[] { "open", "done", "all" }, @default = "open" } },
                            new { name = "folder", @in = "query", schema = new { type = "string" } },
                            new { name = "updatedSince", @in = "query", schema = new { type = "string", format = "date-time" } },
                            new { name = "limit", @in = "query", schema = new { type = "integer" } },
                            new { name = "cursor", @in = "query", schema = new { type = "string" } }
                        },
                        responses = new
                        {
                            @default = new
                            {
                                description = "Task page",
                                content = new
                                {
                                    application_json = new
                                    {
                                        schema = new
                                        {
                                            type = "object",
                                            properties = new
                                            {
                                                tasks = new { type = "array", items = task },
                                                nextCursor = new { type = "string", nullable = true }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    post = new
                    {
                        summary = "Create standalone todo (Todos.md); requires wholeVault",
                        security = Scope("tasks:write"),
                        parameters = new object[]
                        {
                            new { name = "Idempotency-Key", @in = "header", required = true, schema = new { type = "string" } }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new
                            {
                                application_json = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "text" },
                                        properties = new
                                        {
                                            text = new { type = "string" },
                                            dueDate = new { type = "string", format = "date-time" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                ["/api/integrations/v1/tasks/{taskId}"] = new
                {
                    patch = new
                    {
                        summary = "Patch task done/text (If-Match = containing note etag)",
                        security = Scope("tasks:write"),
                        parameters = new object[]
                        {
                            new { name = "If-Match", @in = "header", required = true, schema = new { type = "string" } }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new
                            {
                                application_json = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            text = new { type = "string" },
                                            done = new { type = "boolean" },
                                            dueDate = new { type = "string", format = "date-time" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            components = new
            {
                schemas = new { Note = note, Task = task },
                securitySchemes = new
                {
                    bearerAuth = new { type = "http", scheme = "bearer", bearerFormat = "JotdexIntegrationToken" }
                }
            },
            security = new[] { new { bearerAuth = Array.Empty<string>() } }
        };
    }

    private static object[] Scope(string scope) =>
    [
        new Dictionary<string, string[]> { ["bearerAuth"] = [scope] }
    ];

    private sealed class ConfigBody
    {
        public bool? Enabled { get; set; }
        public int? ReadRequestsPerMinute { get; set; }
        public int? WriteRequestsPerMinute { get; set; }
    }

    private sealed class CreateTokenBody
    {
        public string? Name { get; set; }
        public List<string>? Scopes { get; set; }
        public List<string>? AllowedFolderRoots { get; set; }
        public bool WholeVault { get; set; }
        public int? ExpirationDays { get; set; }
        public bool NeverExpires { get; set; }
        public string? AdminPassword { get; set; }
        public string? TotpCode { get; set; }
    }

    private static IEnumerable<string> FlattenFolders(FolderNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.RelativePath))
            yield return node.RelativePath;
        foreach (var child in node.Children)
        foreach (var p in FlattenFolders(child))
            yield return p;
    }

    private sealed class CreateNoteBody
    {
        public string? Title { get; set; }
        public string? FolderPath { get; set; }
        public string? BodyMarkdown { get; set; }
    }

    private sealed class BodyOnly
    {
        public string? BodyMarkdown { get; set; }
    }

    private sealed class InsertBody
    {
        public string? Markdown { get; set; }
        public string? Position { get; set; }
        public string? Heading { get; set; }
        public int? Occurrence { get; set; }
    }

    private sealed class CreateTaskBody
    {
        public string? Text { get; set; }
        public string? DueDate { get; set; }
    }

    private sealed class PatchTaskBody
    {
        public string? Text { get; set; }
        public bool? Done { get; set; }
        public string? DueDate { get; set; }
    }
}
