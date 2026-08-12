using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jotdex.Core.CloudBackup;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup.Providers;

public sealed class OneDriveCloudBackupProvider : ICloudBackupProvider
{
    public const string ClientIdEnv = "JOTDEX_CLOUD_ONEDRIVE_CLIENT_ID";
    public const string RedirectUriEnv = "JOTDEX_CLOUD_ONEDRIVE_REDIRECT_URI";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudCredentialStore _credentials;
    private readonly ICloudOAuthClientConfig _oauthConfig;
    private readonly ILogger<OneDriveCloudBackupProvider> _logger;
    private string? _accessToken;
    private DateTimeOffset _accessExpires = DateTimeOffset.MinValue;

    public OneDriveCloudBackupProvider(
        IHttpClientFactory httpClientFactory,
        ICloudCredentialStore credentials,
        ICloudOAuthClientConfig oauthConfig,
        ILogger<OneDriveCloudBackupProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
        _oauthConfig = oauthConfig;
        _logger = logger;
    }

    public CloudProviderKind Kind => CloudProviderKind.OneDrive;

    public bool IsConfiguredInBuild => _oauthConfig.IsConfigured(Kind);

    public async Task<CloudProviderAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new CloudProviderAccount
        {
            AccountId = doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            DisplayName = doc.RootElement.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            Email = doc.RootElement.TryGetProperty("mail", out var mail)
                ? mail.GetString()
                : doc.RootElement.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null
        };
    }

    public async Task<CloudProviderQuota> GetQuotaAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();

        // App Folder scope (Files.ReadWrite.AppFolder) cannot read /me/drive — only special/approot.
        // Prefer quota from the drive metadata when available; otherwise return empty (upload still works).
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive?$select=quota");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogDebug("OneDrive drive quota unavailable ({Status}); continuing without quota (App Folder scope).",
                (int)res.StatusCode);
            return new CloudProviderQuota();
        }

        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("quota", out var q))
            return new CloudProviderQuota();
        long? total = q.TryGetProperty("total", out var t) ? t.GetInt64() : null;
        long? used = q.TryGetProperty("used", out var u) ? u.GetInt64() : null;
        long? remaining = q.TryGetProperty("remaining", out var r) ? r.GetInt64() : null;
        return new CloudProviderQuota { TotalBytes = total, UsedBytes = used, RemainingBytes = remaining };
    }

    public async Task<CloudRemoteRoot> EnsureBackupRootAsync(CloudBackupContext context, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();

        // App Folder special folder (recreate path under approot if needed)
        using var approotReq = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive/special/approot");
        approotReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var approotRes = await client.SendAsync(approotReq, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(approotRes, cancellationToken).ConfigureAwait(false);
        using var approotDoc = await JsonDocument.ParseAsync(await approotRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var approotId = approotDoc.RootElement.GetProperty("id").GetString()!;

        if (!string.IsNullOrWhiteSpace(context.ExistingRemoteRootId))
        {
            using var check = new HttpRequestMessage(HttpMethod.Get,
                $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(context.ExistingRemoteRootId)}?$select=id,name,folder");
            check.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            using var checkRes = await client.SendAsync(check, cancellationToken).ConfigureAwait(false);
            if (checkRes.IsSuccessStatusCode)
            {
                using var checkDoc = await JsonDocument.ParseAsync(
                    await checkRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (checkDoc.RootElement.TryGetProperty("folder", out _))
                {
                    return new CloudRemoteRoot
                    {
                        RootId = context.ExistingRemoteRootId!,
                        DisplayPath = $"Apps/Jotdex/Backups/{context.BackupSetId}"
                    };
                }
            }
            // Missing/deleted — recreate under approot
        }

        var folder = await EnsureChildFolderAsync(approotId, "Backups", cancellationToken).ConfigureAwait(false);
        var setFolder = await EnsureChildFolderAsync(folder, context.BackupSetId, cancellationToken).ConfigureAwait(false);
        return new CloudRemoteRoot
        {
            RootId = setFolder,
            DisplayPath = $"Apps/Jotdex/Backups/{context.BackupSetId}"
        };
    }

    public async Task<CloudRemoteFile> UploadAsync(
        CloudUploadRequest request,
        IProgress<CloudUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var info = new FileInfo(request.LocalPath);
        var client = ApiClient();

        // Upload session
        var sessionUrl =
            $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(request.Root.RootId)}:/{Uri.EscapeDataString(request.FileName)}:/createUploadSession";
        using var sessionReq = new HttpRequestMessage(HttpMethod.Post, sessionUrl);
        sessionReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        sessionReq.Content = JsonContent.Create(new
        {
            item = new Dictionary<string, object>
            {
                ["@microsoft.graph.conflictBehavior"] = "replace",
                ["name"] = request.FileName
            }
        });
        using var sessionRes = await client.SendAsync(sessionReq, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(sessionRes, cancellationToken).ConfigureAwait(false);
        using var sessionDoc = await JsonDocument.ParseAsync(await sessionRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var uploadUrl = sessionDoc.RootElement.GetProperty("uploadUrl").GetString()!;

        const int chunk = 5 * 1024 * 1024;
        await using var stream = File.OpenRead(request.LocalPath);
        var buffer = new byte[chunk];
        long offset = 0;
        HttpResponseMessage? last = null;
        while (offset < info.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(chunk, info.Length - offset)), cancellationToken)
                .ConfigureAwait(false);
            using var put = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
            put.Content = new ByteArrayContent(buffer, 0, read);
            put.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + read - 1, info.Length);
            put.Content.Headers.ContentLength = read;
            last?.Dispose();
            last = await client.SendAsync(put, cancellationToken).ConfigureAwait(false);
            if (!last.IsSuccessStatusCode && (int)last.StatusCode != 202)
                await EnsureOkAsync(last, cancellationToken).ConfigureAwait(false);
            offset += read;
            progress?.Report(new CloudUploadProgress { BytesSent = offset, TotalBytes = info.Length });
        }

        using var final = last ?? throw new CloudBackupProviderException(CloudBackupFailureCode.UploadFailed, "Empty OneDrive upload.");
        await EnsureOkAsync(final, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await final.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var size = doc.RootElement.TryGetProperty("size", out var s) ? s.GetInt64() : info.Length;
        if (size != info.Length)
        {
            throw new CloudBackupProviderException(
                CloudBackupFailureCode.RemoteSizeMismatch,
                $"OneDrive size mismatch (local={info.Length}, remote={size}).");
        }

        string? remoteSha256 = null;
        if (doc.RootElement.TryGetProperty("file", out var fileEl) &&
            fileEl.TryGetProperty("hashes", out var hashes) &&
            hashes.TryGetProperty("sha256Hash", out var shaEl))
        {
            remoteSha256 = shaEl.GetString();
            if (!string.IsNullOrWhiteSpace(remoteSha256) &&
                !string.IsNullOrWhiteSpace(request.Sha256) &&
                !string.Equals(remoteSha256, request.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CloudBackupProviderException(
                    CloudBackupFailureCode.RemoteChecksumMismatch,
                    $"OneDrive sha256Hash mismatch (local={request.Sha256}, remote={remoteSha256}).");
            }
        }

        return new CloudRemoteFile
        {
            FileId = doc.RootElement.GetProperty("id").GetString()!,
            FileName = request.FileName,
            SizeBytes = size,
            ContentHash = remoteSha256 ?? request.Sha256,
            ModifiedUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<CloudRemoteFile?> GetMetadataAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(file.FileId)}?$select=id,name,size,file");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new CloudRemoteFile
        {
            FileId = doc.RootElement.GetProperty("id").GetString()!,
            FileName = doc.RootElement.GetProperty("name").GetString() ?? "",
            SizeBytes = doc.RootElement.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
            ContentHash = doc.RootElement.TryGetProperty("file", out var fileEl) &&
                          fileEl.TryGetProperty("hashes", out var hashes) &&
                          hashes.TryGetProperty("sha256Hash", out var sha)
                ? sha.GetString()
                : null
        };
    }

    public async Task<Stream> OpenDownloadAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(file.FileId)}/content");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        return await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CloudRemoteFile>> ListBackupsAsync(CloudRemoteRoot root, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(root.RootId)}/children?$select=id,name,size&$top=200");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var list = new List<CloudRemoteFile>();
        if (doc.RootElement.TryGetProperty("value", out var value))
        {
            foreach (var f in value.EnumerateArray())
            {
                if (!f.TryGetProperty("file", out _) && f.TryGetProperty("folder", out _)) continue;
                list.Add(new CloudRemoteFile
                {
                    FileId = f.GetProperty("id").GetString()!,
                    FileName = f.GetProperty("name").GetString() ?? "",
                    SizeBytes = f.TryGetProperty("size", out var s) ? s.GetInt64() : 0
                });
            }
        }
        return list;
    }

    public async Task DeleteAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Delete,
            $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(file.FileId)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> EnsureChildFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var client = ApiClient();
        // Try create; on conflict, list and find
        using var create = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(parentId)}/children");
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        create.Content = new StringContent(
            $"{{\"name\":{JsonSerializer.Serialize(name)},\"folder\":{{}},\"@microsoft.graph.conflictBehavior\":\"fail\"}}",
            Encoding.UTF8, "application/json");
        using var res = await client.SendAsync(create, ct).ConfigureAwait(false);
        if (res.IsSuccessStatusCode)
        {
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            return doc.RootElement.GetProperty("id").GetString()!;
        }

        using var listReq = new HttpRequestMessage(HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(parentId)}/children?$select=id,name,folder&$filter=name eq '{name.Replace("'", "''")}'");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var listRes = await client.SendAsync(listReq, ct).ConfigureAwait(false);
        await EnsureOkAsync(listRes, ct).ConfigureAwait(false);
        using var listDoc = await JsonDocument.ParseAsync(await listRes.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);
        if (listDoc.RootElement.TryGetProperty("value", out var value))
        {
            foreach (var item in value.EnumerateArray())
            {
                if (string.Equals(item.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                    return item.GetProperty("id").GetString()!;
            }
        }

        throw new CloudBackupProviderException(CloudBackupFailureCode.ProviderUnavailable, "Could not create OneDrive backup folder.");
    }

    private void EnsureConfigured()
    {
        if (!IsConfiguredInBuild)
            throw CloudBackupProviderException.ConfigurationMissing(Kind);
    }

    private async Task EnsureAccessTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessToken) && _accessExpires > DateTimeOffset.UtcNow.AddMinutes(2))
            return;
        if (!_credentials.TryGet(Kind, out var cred) || cred is null || string.IsNullOrWhiteSpace(cred.ProtectedPayload))
            throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "OneDrive is not connected.");

        // Payload JSON: { "refresh_token": "...", "access_token": "...", "expires_utc": "..." }
        string refresh;
        try
        {
            using var tmp = JsonDocument.Parse(cred.ProtectedPayload);
            if (tmp.RootElement.TryGetProperty("access_token", out var at) &&
                tmp.RootElement.TryGetProperty("expires_utc", out var exp) &&
                DateTimeOffset.TryParse(exp.GetString(), out var expUtc) &&
                expUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                _accessToken = at.GetString();
                _accessExpires = expUtc;
                return;
            }
            refresh = tmp.RootElement.GetProperty("refresh_token").GetString()
                      ?? throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "OneDrive refresh token missing.");
        }
        catch (CloudBackupProviderException) { throw; }
        catch
        {
            refresh = cred.ProtectedPayload;
        }

        var clientId = _oauthConfig.GetClientId(Kind)
                       ?? throw CloudBackupProviderException.ConfigurationMissing(Kind);
        var client = ApiClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh,
            ["scope"] = "offline_access Files.ReadWrite.AppFolder User.Read"
        });
        using var res = await client.PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", content, ct)
            .ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "OneDrive authorization expired.");
            throw new CloudBackupProviderException(CloudBackupFailureCode.TokenRefreshFailed, "OneDrive token refresh failed.");
        }
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600;
        _accessExpires = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        var newRefresh = doc.RootElement.TryGetProperty("refresh_token", out var nr) ? nr.GetString() : refresh;
        _credentials.Set(Kind, new CloudCredentialEnvelope
        {
            Provider = Kind,
            AccountId = cred.AccountId,
            AccountDisplayName = cred.AccountDisplayName,
            AccountEmail = cred.AccountEmail,
            ProtectedPayload = JsonSerializer.Serialize(new
            {
                refresh_token = newRefresh,
                access_token = _accessToken,
                expires_utc = _accessExpires
            }),
            UpdatedUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task EnsureOkAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "OneDrive authorization expired.");
        if ((int)res.StatusCode == 429)
            throw new CloudBackupProviderException(CloudBackupFailureCode.RateLimited, "OneDrive rate limited.");
        if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("OneDrive API 403: {Body}", body.Length > 300 ? body[..300] : body);
            throw new CloudBackupProviderException(
                CloudBackupFailureCode.AuthorizationDenied,
                "OneDrive access denied (403). In Azure app permissions, add Microsoft Graph delegated Files.ReadWrite.AppFolder + User.Read + offline_access, grant consent, then Disconnect and Connect again.");
        }

        _logger.LogWarning("OneDrive API {Status}: {Body}", (int)res.StatusCode, body.Length > 200 ? body[..200] : body);
        throw new CloudBackupProviderException(CloudBackupFailureCode.ProviderUnavailable, $"OneDrive API error {(int)res.StatusCode}");
    }

    private HttpClient ApiClient()
    {
        var client = _httpClientFactory.CreateClient("cloud-onedrive");
        client.Timeout = TimeSpan.FromMinutes(30);
        return client;
    }
}
