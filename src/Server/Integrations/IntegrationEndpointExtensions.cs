using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Jotdex.Core.Auth;
using Jotdex.Core.Integrations;
using Jotdex.Core.Search;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Integrations;
using Jotdex.Infrastructure.Vault;
using Jotdex.Server.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;

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
                var max = ctx.HttpContext.RequestServices.GetRequiredService<IIntegrationConfigService>().Get().MaxBodyBytes;
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
            IIntegrationFolderAccess acl, IIntegrationConfigService cfg, string? q, string? folder, int? limit) =>
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
                hits.Add(new
                {
                    id = h.NoteId,
                    title = h.Title,
                    relativePath = h.RelativePath,
                    folderPath = h.FolderPath,
                    snippet = Truncate(h.Snippet, 280)
                });
                if (hits.Count >= max) break;
            }
            return Results.Json(new { hits, query = q });
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

        reads.MapGet("/openapi.json", () => Results.Json(OpenApiDocument()));
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

    private static object OpenApiDocument() => new
    {
        openapi = "3.0.3",
        info = new { title = "Jotdex Integrations API", version = "v1" },
        paths = new Dictionary<string, object>
        {
            ["/api/integrations/v1/whoami"] = new { get = new { summary = "Verify token" } },
            ["/api/integrations/v1/folders"] = new { get = new { summary = "List permitted folders" } },
            ["/api/integrations/v1/notes"] = new { get = new { summary = "List notes" }, post = new { summary = "Create note" } },
            ["/api/integrations/v1/search"] = new { get = new { summary = "Search notes" } },
            ["/api/integrations/v1/notes/{id}"] = new { get = new { summary = "Get note" }, put = new { summary = "Replace body" } },
            ["/api/integrations/v1/notes/{id}/append"] = new { post = new { summary = "Append body" } },
            ["/api/integrations/v1/notes/{id}/attachments/{attachmentId}"] = new { get = new { summary = "Download attachment" } }
        },
        components = new
        {
            securitySchemes = new
            {
                bearerAuth = new { type = "http", scheme = "bearer", bearerFormat = "JotdexIntegrationToken" }
            }
        },
        security = new[] { new { bearerAuth = Array.Empty<string>() } }
    };

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
}
