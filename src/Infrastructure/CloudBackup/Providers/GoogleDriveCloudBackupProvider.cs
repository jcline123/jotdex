using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jotdex.Core.CloudBackup;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup.Providers;

public sealed class GoogleDriveCloudBackupProvider : ICloudBackupProvider
{
    public const string ClientIdEnv = "JOTDEX_CLOUD_GOOGLE_CLIENT_ID";
    public const string ClientSecretEnv = "JOTDEX_CLOUD_GOOGLE_CLIENT_SECRET";
    public const string ClientConfigEnv = "JOTDEX_CLOUD_GOOGLE_CLIENT_CONFIG";
    public const string RedirectUriEnv = "JOTDEX_CLOUD_GOOGLE_REDIRECT_URI";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudCredentialStore _credentials;
    private readonly CloudBackupHashService _hashes;
    private readonly ICloudOAuthClientConfig _oauthConfig;
    private readonly ILogger<GoogleDriveCloudBackupProvider> _logger;
    private string? _accessToken;
    private DateTimeOffset _accessExpires = DateTimeOffset.MinValue;

    public GoogleDriveCloudBackupProvider(
        IHttpClientFactory httpClientFactory,
        ICloudCredentialStore credentials,
        CloudBackupHashService hashes,
        ICloudOAuthClientConfig oauthConfig,
        ILogger<GoogleDriveCloudBackupProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
        _hashes = hashes;
        _oauthConfig = oauthConfig;
        _logger = logger;
    }

    public CloudProviderKind Kind => CloudProviderKind.GoogleDrive;

    public bool IsConfiguredInBuild => _oauthConfig.IsConfigured(Kind);

    public async Task<CloudProviderAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var user = doc.RootElement.GetProperty("user");
        return new CloudProviderAccount
        {
            AccountId = user.TryGetProperty("permissionId", out var id) ? id.GetString() ?? "" : "",
            DisplayName = user.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
            Email = user.TryGetProperty("emailAddress", out var em) ? em.GetString() : null
        };
    }

    public async Task<CloudProviderQuota> GetQuotaAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.googleapis.com/drive/v3/about?fields=storageQuota");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var q = doc.RootElement.GetProperty("storageQuota");
        long? limit = q.TryGetProperty("limit", out var lim) && long.TryParse(lim.GetString(), out var l) ? l : null;
        long? usage = q.TryGetProperty("usage", out var u) && long.TryParse(u.GetString(), out var uu) ? uu : null;
        return new CloudProviderQuota
        {
            TotalBytes = limit,
            UsedBytes = usage,
            RemainingBytes = limit is long t && usage is long used ? t - used : null
        };
    }

    public async Task<CloudRemoteRoot> EnsureBackupRootAsync(CloudBackupContext context, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();

        if (!string.IsNullOrWhiteSpace(context.ExistingRemoteRootId))
        {
            using var check = new HttpRequestMessage(HttpMethod.Get,
                $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(context.ExistingRemoteRootId)}?fields=id,name,trashed,mimeType");
            check.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            using var checkRes = await client.SendAsync(check, cancellationToken).ConfigureAwait(false);
            if (checkRes.IsSuccessStatusCode)
            {
                using var checkDoc = await JsonDocument.ParseAsync(
                    await checkRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var trashed = checkDoc.RootElement.TryGetProperty("trashed", out var tr) && tr.GetBoolean();
                var mime = checkDoc.RootElement.TryGetProperty("mimeType", out var mt) ? mt.GetString() : null;
                if (!trashed && string.Equals(mime, "application/vnd.google-apps.folder", StringComparison.Ordinal))
                {
                    var display = checkDoc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : "Jotdex Backups";
                    return new CloudRemoteRoot { RootId = context.ExistingRemoteRootId!, DisplayPath = display };
                }
            }
            // Folder missing/trashed — recreate below
        }

        var name = string.IsNullOrWhiteSpace(context.BackupSetName)
            ? $"Jotdex Backups - {context.BackupSetId[..Math.Min(8, context.BackupSetId.Length)]}"
            : context.BackupSetName;
        var body = new
        {
            name,
            mimeType = "application/vnd.google-apps.folder",
            appProperties = new Dictionary<string, string>
            {
                ["jotdex"] = "1",
                ["backupSetId"] = context.BackupSetId
            }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files?fields=id,name");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Content = JsonContent.Create(body);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var id = doc.RootElement.GetProperty("id").GetString()!;
        return new CloudRemoteRoot { RootId = id, DisplayPath = name };
    }

    public async Task<CloudRemoteFile> UploadAsync(
        CloudUploadRequest request,
        IProgress<CloudUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var md5 = _hashes.Md5FileHex(request.LocalPath);
        var info = new FileInfo(request.LocalPath);
        var client = ApiClient();

        // Resumable upload session
        var metadata = new
        {
            name = request.FileName,
            parents = new[] { request.Root.RootId },
            appProperties = new Dictionary<string, string>
            {
                ["jotdex"] = "1",
                ["runId"] = request.RunId ?? "",
                ["artifactType"] = request.ArtifactType ?? "",
                ["sha256"] = request.Sha256
            }
        };
        using var start = new HttpRequestMessage(HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id,name,size,md5Checksum");
        start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        start.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
        start.Headers.Add("X-Upload-Content-Length", info.Length.ToString());
        start.Content = JsonContent.Create(metadata);
        using var startRes = await client.SendAsync(start, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(startRes, cancellationToken).ConfigureAwait(false);
        if (!startRes.Headers.TryGetValues("Location", out var locs))
            throw new CloudBackupProviderException(CloudBackupFailureCode.UploadFailed, "Missing Google upload session URL.");
        var sessionUrl = locs.First();

        await using var stream = File.OpenRead(request.LocalPath);
        using var put = new HttpRequestMessage(HttpMethod.Put, sessionUrl);
        put.Content = new StreamContent(stream);
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        put.Content.Headers.ContentLength = info.Length;
        using var putRes = await client.SendAsync(put, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(putRes, cancellationToken).ConfigureAwait(false);
        progress?.Report(new CloudUploadProgress { BytesSent = info.Length, TotalBytes = info.Length });

        using var doc = await JsonDocument.ParseAsync(await putRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var returnedMd5 = doc.RootElement.TryGetProperty("md5Checksum", out var m) ? m.GetString() : null;
        if (!string.IsNullOrWhiteSpace(returnedMd5) &&
            !string.Equals(returnedMd5, md5, StringComparison.OrdinalIgnoreCase))
        {
            throw new CloudBackupProviderException(
                CloudBackupFailureCode.RemoteChecksumMismatch,
                $"Google Drive md5Checksum mismatch (local={md5}, remote={returnedMd5}).");
        }

        return new CloudRemoteFile
        {
            FileId = doc.RootElement.GetProperty("id").GetString()!,
            FileName = request.FileName,
            SizeBytes = doc.RootElement.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var sz) ? sz : info.Length,
            // Prefer server checksum; fall back to local MD5 when Drive omits it.
            Md5Checksum = returnedMd5 ?? md5,
            ContentHash = request.Sha256,
            ModifiedUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<CloudRemoteFile?> GetMetadataAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(file.FileId)}?fields=id,name,size,md5Checksum");
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
            SizeBytes = doc.RootElement.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var sz) ? sz : 0,
            Md5Checksum = doc.RootElement.TryGetProperty("md5Checksum", out var m) ? m.GetString() : null
        };
    }

    public async Task<Stream> OpenDownloadAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(file.FileId)}?alt=media");
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
        var q = $"'{root.RootId}' in parents and trashed=false";
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.googleapis.com/drive/v3/files?pageSize=200&fields=files(id,name,size,md5Checksum)&q=" + Uri.EscapeDataString(q));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var list = new List<CloudRemoteFile>();
        if (doc.RootElement.TryGetProperty("files", out var files))
        {
            foreach (var f in files.EnumerateArray())
            {
                list.Add(new CloudRemoteFile
                {
                    FileId = f.GetProperty("id").GetString()!,
                    FileName = f.GetProperty("name").GetString() ?? "",
                    SizeBytes = f.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var sz) ? sz : 0,
                    Md5Checksum = f.TryGetProperty("md5Checksum", out var m) ? m.GetString() : null
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
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(file.FileId)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
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
            throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "Google Drive is not connected.");

        // Payload: JSON {"refresh_token":"...","access_token":"...","expires_utc":"..."} or raw refresh token
        var refresh = cred.ProtectedPayload;
        try
        {
            using var tmp = JsonDocument.Parse(refresh);
            if (tmp.RootElement.TryGetProperty("access_token", out var at) &&
                tmp.RootElement.TryGetProperty("expires_utc", out var exp) &&
                DateTimeOffset.TryParse(exp.GetString(), out var expUtc) &&
                expUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                _accessToken = at.GetString();
                _accessExpires = expUtc;
                return;
            }
            if (tmp.RootElement.TryGetProperty("refresh_token", out var rt))
                refresh = rt.GetString() ?? refresh;
        }
        catch (JsonException)
        {
            // Raw refresh token
        }

        var clientId = _oauthConfig.GetClientId(Kind) ?? "";
        var client = ApiClient();
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh
        };
        var secret = _oauthConfig.GetClientSecret(Kind);
        if (!string.IsNullOrWhiteSpace(secret))
            fields["client_secret"] = secret;

        using var content = new FormUrlEncodedContent(fields);
        using var res = await client.PostAsync("https://oauth2.googleapis.com/token", content, ct).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw new CloudBackupProviderException(CloudBackupFailureCode.TokenRefreshFailed, "Google token refresh failed.");
        await EnsureOkAsync(res, ct).ConfigureAwait(false);
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
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "Google Drive authorization expired.");
        if ((int)res.StatusCode == 429)
            throw new CloudBackupProviderException(CloudBackupFailureCode.RateLimited, "Google Drive rate limited.");
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _logger.LogWarning("Google Drive API {Status}: {Body}", (int)res.StatusCode, body.Length > 200 ? body[..200] : body);
        throw new CloudBackupProviderException(CloudBackupFailureCode.ProviderUnavailable, $"Google Drive API error {(int)res.StatusCode}");
    }

    private HttpClient ApiClient()
    {
        var client = _httpClientFactory.CreateClient("cloud-google");
        client.Timeout = TimeSpan.FromMinutes(30);
        return client;
    }
}
