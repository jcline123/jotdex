using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jotdex.Core.CloudBackup;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup.Providers;

public sealed class DropboxCloudBackupProvider : ICloudBackupProvider
{
    public const string AppKeyEnv = "JOTDEX_CLOUD_DROPBOX_APP_KEY";
    public const string AppSecretEnv = "JOTDEX_CLOUD_DROPBOX_APP_SECRET";
    public const string RedirectUriEnv = "JOTDEX_CLOUD_DROPBOX_REDIRECT_URI";

    /// <summary>Use upload sessions above this size (Dropbox simple upload max is 150 MiB; 8 MiB is safer).</summary>
    public const long UploadSessionThresholdBytes = 8L * 1024 * 1024;
    public const int UploadSessionChunkBytes = 8 * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudCredentialStore _credentials;
    private readonly CloudBackupHashService _hashes;
    private readonly ICloudOAuthClientConfig _oauthConfig;
    private readonly ILogger<DropboxCloudBackupProvider> _logger;
    private string? _accessToken;
    private DateTimeOffset _accessExpires = DateTimeOffset.MinValue;

    public DropboxCloudBackupProvider(
        IHttpClientFactory httpClientFactory,
        ICloudCredentialStore credentials,
        CloudBackupHashService hashes,
        ICloudOAuthClientConfig oauthConfig,
        ILogger<DropboxCloudBackupProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
        _hashes = hashes;
        _oauthConfig = oauthConfig;
        _logger = logger;
    }

    public CloudProviderKind Kind => CloudProviderKind.Dropbox;

    public bool IsConfiguredInBuild => _oauthConfig.IsConfigured(Kind);

    public async Task<CloudProviderAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_current_account");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = doc.RootElement;
        return new CloudProviderAccount
        {
            AccountId = root.GetProperty("account_id").GetString() ?? "",
            DisplayName = root.TryGetProperty("name", out var name) && name.TryGetProperty("display_name", out var dn)
                ? dn.GetString()
                : null,
            Email = root.TryGetProperty("email", out var email) ? email.GetString() : null
        };
    }

    public async Task<CloudProviderQuota> GetQuotaAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_space_usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var used = doc.RootElement.GetProperty("used").GetInt64();
        long? total = null;
        if (doc.RootElement.TryGetProperty("allocation", out var alloc) &&
            alloc.TryGetProperty("allocated", out var allocated))
            total = allocated.GetInt64();
        return new CloudProviderQuota
        {
            UsedBytes = used,
            TotalBytes = total,
            RemainingBytes = total is long t ? t - used : null
        };
    }

    public async Task<CloudRemoteRoot> EnsureBackupRootAsync(CloudBackupContext context, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        // App Folder root: paths are relative to the app's folder (Apps/Jotdex/...).
        var path = $"/Backups/{context.BackupSetId}";
        await CreateFolderIfNeededAsync("/Backups", cancellationToken).ConfigureAwait(false);
        await CreateFolderIfNeededAsync(path, cancellationToken).ConfigureAwait(false);
        return new CloudRemoteRoot { RootId = path, DisplayPath = "Apps/Jotdex" + path };
    }

    public async Task<CloudRemoteFile> UploadAsync(
        CloudUploadRequest request,
        IProgress<CloudUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var dropboxPath = Combine(request.Root.RootId, request.FileName);
        var contentHash = _hashes.DropboxContentHashHex(request.LocalPath);
        var info = new FileInfo(request.LocalPath);

        JsonDocument doc;
        if (info.Length <= UploadSessionThresholdBytes)
            doc = await SimpleUploadAsync(dropboxPath, request.LocalPath, info.Length, cancellationToken).ConfigureAwait(false);
        else
            doc = await SessionUploadAsync(dropboxPath, request.LocalPath, info.Length, progress, cancellationToken).ConfigureAwait(false);

        using (doc)
        {
            progress?.Report(new CloudUploadProgress { BytesSent = info.Length, TotalBytes = info.Length });

            var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? dropboxPath : dropboxPath;
            var size = doc.RootElement.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : info.Length;
            var remoteHash = doc.RootElement.TryGetProperty("content_hash", out var ch) ? ch.GetString() : null;
            if (string.IsNullOrWhiteSpace(remoteHash) ||
                !string.Equals(remoteHash, contentHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new CloudBackupProviderException(
                    CloudBackupFailureCode.RemoteChecksumMismatch,
                    $"Dropbox content_hash mismatch (local={contentHash}, remote={remoteHash ?? "missing"}).");
            }

            return new CloudRemoteFile
            {
                FileId = id,
                FileName = request.FileName,
                SizeBytes = size,
                ContentHash = remoteHash,
                ModifiedUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public async Task<CloudRemoteFile?> GetMetadataAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var path = ResolvePath(file);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/get_metadata");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Content = JsonContent.Create(new { path, include_deleted = false });
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.Conflict ||
            res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new CloudRemoteFile
        {
            FileId = doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? path : path,
            FileName = Path.GetFileName(doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? path : path),
            SizeBytes = doc.RootElement.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
            ContentHash = doc.RootElement.TryGetProperty("content_hash", out var ch) ? ch.GetString() : null,
            ModifiedUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<Stream> OpenDownloadAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var path = ResolvePath(file);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/download");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { path }));
        var client = ApiClient();
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
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/list_folder");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Content = JsonContent.Create(new { path = root.RootId, recursive = false });
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var list = new List<CloudRemoteFile>();
        if (doc.RootElement.TryGetProperty("entries", out var entries))
        {
            foreach (var e in entries.EnumerateArray())
            {
                if (!e.TryGetProperty(".tag", out var tag) || tag.GetString() != "file") continue;
                list.Add(new CloudRemoteFile
                {
                    FileId = e.GetProperty("id").GetString() ?? "",
                    FileName = e.GetProperty("name").GetString() ?? "",
                    SizeBytes = e.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                    ContentHash = e.TryGetProperty("content_hash", out var ch) ? ch.GetString() : null
                });
            }
        }
        return list;
    }

    public async Task DeleteAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var path = ResolvePath(file);
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/delete_v2");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Content = JsonContent.Create(new { path });
        using var res = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> SimpleUploadAsync(
        string dropboxPath, string localPath, long length, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(localPath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = length;

        var arg = JsonSerializer.Serialize(new
        {
            path = dropboxPath,
            mode = "overwrite",
            autorename = false,
            mute = true,
            strict_conflict = false
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.Add("Dropbox-API-Arg", arg);
        req.Content = content;

        var client = ApiClient();
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureOkAsync(res, cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<JsonDocument> SessionUploadAsync(
        string dropboxPath,
        string localPath,
        long length,
        IProgress<CloudUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var client = ApiClient();
        await using var stream = File.OpenRead(localPath);
        var buffer = new byte[UploadSessionChunkBytes];
        long offset = 0;
        string? sessionId = null;

        while (offset < length)
        {
            var toRead = (int)Math.Min(UploadSessionChunkBytes, length - offset);
            var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read <= 0) break;

            var isFirst = sessionId is null;
            var isLast = offset + read >= length;
            byte[] chunk = read == buffer.Length ? buffer : buffer.AsSpan(0, read).ToArray();

            if (isFirst && isLast)
            {
                // Single-chunk session: start with close=true then finish — use start+finish via start close + finish empty.
                using var startContent = new ByteArrayContent(chunk);
                startContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var startReq = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload_session/start");
                startReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                startReq.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { close = true }));
                startReq.Content = startContent;
                using var startRes = await client.SendAsync(startReq, cancellationToken).ConfigureAwait(false);
                await EnsureOkAsync(startRes, cancellationToken).ConfigureAwait(false);
                using var startDoc = await JsonDocument.ParseAsync(await startRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                sessionId = startDoc.RootElement.GetProperty("session_id").GetString()!;
                offset += read;
                progress?.Report(new CloudUploadProgress { BytesSent = offset, TotalBytes = length });
                return await FinishSessionAsync(client, sessionId, offset, dropboxPath, cancellationToken).ConfigureAwait(false);
            }

            if (isFirst)
            {
                using var startContent = new ByteArrayContent(chunk);
                startContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var startReq = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload_session/start");
                startReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                startReq.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { close = false }));
                startReq.Content = startContent;
                using var startRes = await client.SendAsync(startReq, cancellationToken).ConfigureAwait(false);
                await EnsureOkAsync(startRes, cancellationToken).ConfigureAwait(false);
                using var startDoc = await JsonDocument.ParseAsync(await startRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                sessionId = startDoc.RootElement.GetProperty("session_id").GetString()!;
            }
            else if (!isLast)
            {
                using var appendContent = new ByteArrayContent(chunk);
                appendContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var appendReq = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload_session/append_v2");
                appendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                appendReq.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new
                {
                    cursor = new { session_id = sessionId, offset },
                    close = false
                }));
                appendReq.Content = appendContent;
                using var appendRes = await client.SendAsync(appendReq, cancellationToken).ConfigureAwait(false);
                await EnsureOkAsync(appendRes, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Last chunk via finish
                using var finishContent = new ByteArrayContent(chunk);
                finishContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var finishReq = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload_session/finish");
                finishReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                finishReq.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new
                {
                    cursor = new { session_id = sessionId, offset },
                    commit = new
                    {
                        path = dropboxPath,
                        mode = "overwrite",
                        autorename = false,
                        mute = true,
                        strict_conflict = false
                    }
                }));
                finishReq.Content = finishContent;
                using var finishRes = await client.SendAsync(finishReq, cancellationToken).ConfigureAwait(false);
                await EnsureOkAsync(finishRes, cancellationToken).ConfigureAwait(false);
                offset += read;
                progress?.Report(new CloudUploadProgress { BytesSent = offset, TotalBytes = length });
                return await JsonDocument.ParseAsync(await finishRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            offset += read;
            progress?.Report(new CloudUploadProgress { BytesSent = offset, TotalBytes = length });
        }

        if (sessionId is null)
            throw new CloudBackupProviderException(CloudBackupFailureCode.UploadFailed, "Empty Dropbox upload.");

        return await FinishSessionAsync(client, sessionId, offset, dropboxPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> FinishSessionAsync(
        HttpClient client, string sessionId, long offset, string dropboxPath, CancellationToken cancellationToken)
    {
        using var finishContent = new ByteArrayContent(Array.Empty<byte>());
        finishContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var finishReq = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload_session/finish");
        finishReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        finishReq.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new
        {
            cursor = new { session_id = sessionId, offset },
            commit = new
            {
                path = dropboxPath,
                mode = "overwrite",
                autorename = false,
                mute = true,
                strict_conflict = false
            }
        }));
        finishReq.Content = finishContent;
        using var finishRes = await client.SendAsync(finishReq, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(finishRes, cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(await finishRes.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
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
            throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "Dropbox is not connected.");

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
            // Raw refresh token from older placeholder completions
        }

        var appKey = _oauthConfig.GetClientId(Kind)
                     ?? throw CloudBackupProviderException.ConfigurationMissing(Kind);
        var client = ApiClient();
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh,
            ["client_id"] = appKey
        };
        var secret = _oauthConfig.GetClientSecret(Kind);
        if (!string.IsNullOrWhiteSpace(secret))
            fields["client_secret"] = secret;

        using var content = new FormUrlEncodedContent(fields);
        using var res = await client.PostAsync("https://api.dropboxapi.com/oauth2/token", content, ct).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            res.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw new CloudBackupProviderException(CloudBackupFailureCode.TokenRefreshFailed, "Dropbox token refresh failed.");
        await EnsureOkAsync(res, ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
            .ConfigureAwait(false);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 14400;
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

    private async Task CreateFolderIfNeededAsync(string path, CancellationToken ct)
    {
        var client = ApiClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/create_folder_v2");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Content = JsonContent.Create(new { path, autorename = false });
        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode && res.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!body.Contains("path/conflict", StringComparison.OrdinalIgnoreCase))
                await EnsureOkAsync(res, ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureOkAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "Dropbox authorization expired.");
        if ((int)res.StatusCode == 429)
            throw new CloudBackupProviderException(CloudBackupFailureCode.RateLimited, "Dropbox rate limited.");
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _logger.LogWarning("Dropbox API {Status}: {Body}", (int)res.StatusCode, Truncate(body));
        throw new CloudBackupProviderException(CloudBackupFailureCode.ProviderUnavailable,
            $"Dropbox API error {(int)res.StatusCode}");
    }

    private HttpClient ApiClient()
    {
        var client = _httpClientFactory.CreateClient("cloud-dropbox");
        client.Timeout = TimeSpan.FromMinutes(30);
        return client;
    }

    private static string ResolvePath(CloudRemoteFileReference file)
    {
        // Prefer Dropbox id:… or absolute App Folder path over bare file names.
        if (!string.IsNullOrWhiteSpace(file.FileId) &&
            (file.FileId.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ||
             file.FileId.StartsWith('/')))
            return file.FileId;
        if (!string.IsNullOrWhiteSpace(file.PathOrName) && file.PathOrName.StartsWith('/'))
            return file.PathOrName;
        if (!string.IsNullOrWhiteSpace(file.FileId))
            return file.FileId;
        return file.PathOrName ?? "";
    }

    private static string Combine(string root, string fileName)
    {
        var r = root.TrimEnd('/');
        return r + "/" + fileName.TrimStart('/');
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];
}
