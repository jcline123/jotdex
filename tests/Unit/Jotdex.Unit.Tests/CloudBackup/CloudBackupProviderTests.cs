using System.Net;
using System.Text;
using System.Text.Json;
using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup;
using Jotdex.Infrastructure.CloudBackup.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Unit.Tests.CloudBackup;

public class DropboxCloudBackupProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string? _prevKey;
    private readonly string? _prevSecret;

    public DropboxCloudBackupProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-dbx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _prevKey = Environment.GetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv);
        _prevSecret = Environment.GetEnvironmentVariable(DropboxCloudBackupProvider.AppSecretEnv);
        Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv, "test-dropbox-app-key");
        Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppSecretEnv, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv, _prevKey);
        Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppSecretEnv, _prevSecret);
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void IsConfiguredInBuild_false_when_app_key_missing()
    {
        Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv, null);
        var provider = CreateProvider(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.False(provider.IsConfiguredInBuild);
        var ex = Assert.Throws<CloudBackupProviderException>(() =>
            provider.GetAccountAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Equal(CloudBackupFailureCode.ProviderConfigurationMissing, ex.Code);
    }

    [Fact]
    public async Task Refresh_token_exchange_then_list()
    {
        var handler = new RecordingHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/oauth2/token", StringComparison.Ordinal))
            {
                return Json(new { access_token = "access-1", expires_in = 3600, token_type = "bearer" });
            }

            if (req.RequestUri.AbsolutePath.Contains("/files/list_folder", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                Assert.Equal("access-1", req.Headers.Authorization?.Parameter);
                return Json(new
                {
                    entries = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            [".tag"] = "file",
                            ["id"] = "id:abc",
                            ["name"] = "kit.jotdexkit",
                            ["size"] = 12,
                            ["content_hash"] = "deadbeef"
                        }
                    }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(req.RequestUri.ToString()) };
        });

        var creds = new MemoryCredentialStore();
        creds.Set(CloudProviderKind.Dropbox, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.Dropbox,
            ProtectedPayload = JsonSerializer.Serialize(new { refresh_token = "refresh-xyz" })
        });

        var provider = CreateProvider(handler, creds);
        var list = await provider.ListBackupsAsync(new CloudRemoteRoot { RootId = "/Backups/set1" }, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("kit.jotdexkit", list[0].FileName);
        Assert.Contains(handler.Requests, r => r.Contains("/oauth2/token", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, r => r.Contains("/files/list_folder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Small_upload_verifies_content_hash()
    {
        var path = Path.Combine(_root, "small.bin");
        var bytes = Encoding.UTF8.GetBytes("Hello, World!\n");
        await File.WriteAllBytesAsync(path, bytes);
        var expectedHash = new CloudBackupHashService().DropboxContentHashHex(path);

        var handler = new RecordingHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/oauth2/token", StringComparison.Ordinal))
                return Json(new { access_token = "access-1", expires_in = 3600 });

            if (req.RequestUri.AbsolutePath.Contains("/files/upload", StringComparison.Ordinal) &&
                !req.RequestUri.AbsolutePath.Contains("upload_session", StringComparison.Ordinal))
            {
                return Json(new
                {
                    id = "id:uploaded",
                    name = "small.bin",
                    size = bytes.Length,
                    content_hash = expectedHash
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new MemoryCredentialStore();
        creds.Set(CloudProviderKind.Dropbox, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.Dropbox,
            ProtectedPayload = JsonSerializer.Serialize(new
            {
                refresh_token = "r",
                access_token = "access-1",
                expires_utc = DateTimeOffset.UtcNow.AddHours(1)
            })
        });

        var provider = CreateProvider(handler, creds);
        var remote = await provider.UploadAsync(new CloudUploadRequest
        {
            Root = new CloudRemoteRoot { RootId = "/Backups/set1" },
            FileName = "small.bin",
            LocalPath = path,
            Sha256 = "abc"
        }, null, CancellationToken.None);

        Assert.Equal("id:uploaded", remote.FileId);
        Assert.Equal(expectedHash, remote.ContentHash);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("upload_session", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_throws_on_content_hash_mismatch()
    {
        var path = Path.Combine(_root, "bad.bin");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("data"));

        var handler = new RecordingHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/files/upload", StringComparison.Ordinal))
            {
                return Json(new
                {
                    id = "id:x",
                    name = "bad.bin",
                    size = 4,
                    content_hash = "0000000000000000000000000000000000000000000000000000000000000000"
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new MemoryCredentialStore();
        creds.Set(CloudProviderKind.Dropbox, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.Dropbox,
            ProtectedPayload = JsonSerializer.Serialize(new
            {
                refresh_token = "r",
                access_token = "access-1",
                expires_utc = DateTimeOffset.UtcNow.AddHours(1)
            })
        });

        var provider = CreateProvider(handler, creds);
        var ex = await Assert.ThrowsAsync<CloudBackupProviderException>(() => provider.UploadAsync(new CloudUploadRequest
        {
            Root = new CloudRemoteRoot { RootId = "/Backups/set1" },
            FileName = "bad.bin",
            LocalPath = path,
            Sha256 = "abc"
        }, null, CancellationToken.None));
        Assert.Equal(CloudBackupFailureCode.RemoteChecksumMismatch, ex.Code);
    }

    private DropboxCloudBackupProvider CreateProvider(RecordingHandler handler, ICloudCredentialStore? creds = null) =>
        new(
            new FixedHttpClientFactory(handler, "cloud-dropbox"),
            creds ?? new MemoryCredentialStore(),
            new CloudBackupHashService(),
            TestOAuthConfig.FromEnvironment(),
            NullLogger<DropboxCloudBackupProvider>.Instance);

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
}

public class GoogleDriveCloudBackupProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string? _prevId;

    public GoogleDriveCloudBackupProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-gdrive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _prevId = Environment.GetEnvironmentVariable(GoogleDriveCloudBackupProvider.ClientIdEnv);
        Environment.SetEnvironmentVariable(GoogleDriveCloudBackupProvider.ClientIdEnv, "test-google-client-id");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(GoogleDriveCloudBackupProvider.ClientIdEnv, _prevId);
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void IsConfiguredInBuild_false_when_client_id_missing()
    {
        Environment.SetEnvironmentVariable(GoogleDriveCloudBackupProvider.ClientIdEnv, null);
        Environment.SetEnvironmentVariable(GoogleDriveCloudBackupProvider.ClientConfigEnv, null);
        var provider = new GoogleDriveCloudBackupProvider(
            new FixedHttpClientFactory(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), "cloud-google"),
            new MemoryCredentialStore(),
            new CloudBackupHashService(),
            TestOAuthConfig.FromEnvironment(),
            NullLogger<GoogleDriveCloudBackupProvider>.Instance);
        Assert.False(provider.IsConfiguredInBuild);
    }

    [Fact]
    public async Task Refresh_then_resumable_upload_verifies_md5()
    {
        var path = Path.Combine(_root, "note.zip");
        var bytes = Encoding.UTF8.GetBytes("vault-bytes");
        await File.WriteAllBytesAsync(path, bytes);
        var md5 = new CloudBackupHashService().Md5FileHex(path);

        var handler = new RecordingHandler(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("oauth2.googleapis.com/token", StringComparison.Ordinal))
                return Json(new { access_token = "g-access", expires_in = 3600 });

            if (req.Method == HttpMethod.Post &&
                req.RequestUri.AbsolutePath.Contains("/upload/drive/v3/files", StringComparison.Ordinal))
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK);
                res.Headers.TryAddWithoutValidation("Location", "https://www.googleapis.com/upload/session/1");
                return res;
            }

            if (req.Method == HttpMethod.Put &&
                req.RequestUri.AbsoluteUri.Contains("/upload/session/1", StringComparison.Ordinal))
            {
                return Json(new
                {
                    id = "file-1",
                    name = "note.zip",
                    size = bytes.Length.ToString(),
                    md5Checksum = md5
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new MemoryCredentialStore();
        creds.Set(CloudProviderKind.GoogleDrive, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.GoogleDrive,
            ProtectedPayload = JsonSerializer.Serialize(new { refresh_token = "g-refresh" })
        });

        var provider = new GoogleDriveCloudBackupProvider(
            new FixedHttpClientFactory(handler, "cloud-google"),
            creds,
            new CloudBackupHashService(),
            TestOAuthConfig.FromEnvironment(),
            NullLogger<GoogleDriveCloudBackupProvider>.Instance);

        var remote = await provider.UploadAsync(new CloudUploadRequest
        {
            Root = new CloudRemoteRoot { RootId = "folder-1" },
            FileName = "note.zip",
            LocalPath = path,
            Sha256 = "sha"
        }, null, CancellationToken.None);

        Assert.Equal("file-1", remote.FileId);
        Assert.Equal(md5, remote.Md5Checksum);
    }

    [Fact]
    public async Task EnsureBackupRoot_reuses_existing_folder_id()
    {
        var handler = new RecordingHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/files/existing-folder", StringComparison.Ordinal))
            {
                return Json(new
                {
                    id = "existing-folder",
                    name = "Jotdex Backups",
                    trashed = false,
                    mimeType = "application/vnd.google-apps.folder"
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new MemoryCredentialStore();
        creds.Set(CloudProviderKind.GoogleDrive, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.GoogleDrive,
            ProtectedPayload = JsonSerializer.Serialize(new
            {
                refresh_token = "r",
                access_token = "a",
                expires_utc = DateTimeOffset.UtcNow.AddHours(1)
            })
        });

        var provider = new GoogleDriveCloudBackupProvider(
            new FixedHttpClientFactory(handler, "cloud-google"),
            creds,
            new CloudBackupHashService(),
            TestOAuthConfig.FromEnvironment(),
            NullLogger<GoogleDriveCloudBackupProvider>.Instance);

        var root = await provider.EnsureBackupRootAsync(new CloudBackupContext
        {
            BackupSetId = "set-1",
            ExistingRemoteRootId = "existing-folder"
        }, CancellationToken.None);

        Assert.Equal("existing-folder", root.RootId);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("uploadType", StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
}

public class OneDriveCloudBackupProviderTests : IDisposable
{
    private readonly string _root;
    private readonly string? _prevId;

    public OneDriveCloudBackupProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-od-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _prevId = Environment.GetEnvironmentVariable(OneDriveCloudBackupProvider.ClientIdEnv);
        Environment.SetEnvironmentVariable(OneDriveCloudBackupProvider.ClientIdEnv, "test-onedrive-client-id");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OneDriveCloudBackupProvider.ClientIdEnv, _prevId);
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void IsConfiguredInBuild_false_when_client_id_missing()
    {
        Environment.SetEnvironmentVariable(OneDriveCloudBackupProvider.ClientIdEnv, null);
        var provider = new OneDriveCloudBackupProvider(
            new FixedHttpClientFactory(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), "cloud-onedrive"),
            new MemoryCredentialStore(),
            TestOAuthConfig.FromEnvironment(),
            NullLogger<OneDriveCloudBackupProvider>.Instance);
        Assert.False(provider.IsConfiguredInBuild);
    }

    [Fact]
    public async Task Api_401_surfaces_AuthenticationRequired()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var creds = new MemoryCredentialStore();
        creds.Set(CloudProviderKind.OneDrive, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.OneDrive,
            ProtectedPayload = JsonSerializer.Serialize(new
            {
                refresh_token = "r",
                access_token = "a",
                expires_utc = DateTimeOffset.UtcNow.AddHours(1)
            })
        });

        var provider = new OneDriveCloudBackupProvider(new FixedHttpClientFactory(handler, "cloud-onedrive"), creds, TestOAuthConfig.FromEnvironment(), NullLogger<OneDriveCloudBackupProvider>.Instance);

        var ex = await Assert.ThrowsAsync<CloudBackupProviderException>(() =>
            provider.GetQuotaAsync(CancellationToken.None));
        Assert.Equal(CloudBackupFailureCode.AuthenticationRequired, ex.Code);
    }

    [Fact]
    public async Task Upload_session_and_list()
    {
        var path = Path.Combine(_root, "kit.bin");
        var bytes = Encoding.UTF8.GetBytes("onedrive-kit");
        await File.WriteAllBytesAsync(path, bytes);
        var sha = new CloudBackupHashService().Sha256FileHex(path);

        var handler = new RecordingHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("createUploadSession", StringComparison.Ordinal))
                return Json(new { uploadUrl = "https://upload.example/session" });

            if (req.RequestUri.Host.Contains("upload.example", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    id = "item-1",
                    name = "kit.bin",
                    size = bytes.Length,
                    file = new { hashes = new { sha256Hash = sha } }
                });
            }

            if (req.RequestUri.AbsolutePath.Contains("/children", StringComparison.Ordinal))
            {
                return Json(new
                {
                    value = new object[]
                    {
                        new { id = "item-1", name = "kit.bin", size = bytes.Length, file = new { } }
                    }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(req.RequestUri.ToString()) };
        });

        var creds = new MemoryCredentialStore();
        creds.Set(CloudProviderKind.OneDrive, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.OneDrive,
            ProtectedPayload = JsonSerializer.Serialize(new
            {
                refresh_token = "r",
                access_token = "a",
                expires_utc = DateTimeOffset.UtcNow.AddHours(1)
            })
        });

        var provider = new OneDriveCloudBackupProvider(new FixedHttpClientFactory(handler, "cloud-onedrive"), creds, TestOAuthConfig.FromEnvironment(), NullLogger<OneDriveCloudBackupProvider>.Instance);

        var remote = await provider.UploadAsync(new CloudUploadRequest
        {
            Root = new CloudRemoteRoot { RootId = "folder-root" },
            FileName = "kit.bin",
            LocalPath = path,
            Sha256 = sha
        }, null, CancellationToken.None);
        Assert.Equal("item-1", remote.FileId);

        var list = await provider.ListBackupsAsync(new CloudRemoteRoot { RootId = "folder-root" }, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("kit.bin", list[0].FileName);
    }

    [Fact]
    public async Task Token_refresh_401_is_AuthenticationRequired()
    {
        var handler = new RecordingHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/token", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new MemoryCredentialStore();
        creds.Set(CloudProviderKind.OneDrive, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.OneDrive,
            ProtectedPayload = JsonSerializer.Serialize(new { refresh_token = "stale" })
        });

        var provider = new OneDriveCloudBackupProvider(new FixedHttpClientFactory(handler, "cloud-onedrive"), creds, TestOAuthConfig.FromEnvironment(), NullLogger<OneDriveCloudBackupProvider>.Instance);

        var ex = await Assert.ThrowsAsync<CloudBackupProviderException>(() =>
            provider.GetAccountAsync(CancellationToken.None));
        Assert.Equal(CloudBackupFailureCode.AuthenticationRequired, ex.Code);
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
}

internal sealed class MemoryCredentialStore : ICloudCredentialStore
{
    private readonly Dictionary<CloudProviderKind, CloudCredentialEnvelope> _map = new();

    public bool TryGet(CloudProviderKind provider, out CloudCredentialEnvelope? credential)
    {
        if (_map.TryGetValue(provider, out var c))
        {
            credential = c;
            return true;
        }

        credential = null;
        return false;
    }

    public void Set(CloudProviderKind provider, CloudCredentialEnvelope credential) => _map[provider] = credential;

    public bool Remove(CloudProviderKind provider) => _map.Remove(provider);

    public bool Has(CloudProviderKind provider) => _map.ContainsKey(provider);
}

internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<string> Requests { get; } = [];

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri?.ToString() ?? "");
        return Task.FromResult(_respond(request));
    }
}

internal sealed class FixedHttpClientFactory(HttpMessageHandler handler, string name) : IHttpClientFactory
{
    public HttpClient CreateClient(string clientName)
    {
        Assert.Equal(name, clientName);
        return new HttpClient(handler, disposeHandler: false);
    }
}

/// <summary>Reads OAuth client ids from process env (same as production fallback).</summary>
internal sealed class TestOAuthConfig : ICloudOAuthClientConfig
{
    public static TestOAuthConfig FromEnvironment() => new();

    public string? GetClientId(CloudProviderKind provider) => provider switch
    {
        CloudProviderKind.Dropbox => Env(DropboxCloudBackupProvider.AppKeyEnv),
        CloudProviderKind.GoogleDrive =>
            Env(GoogleDriveCloudBackupProvider.ClientIdEnv)
            ?? Env(GoogleDriveCloudBackupProvider.ClientConfigEnv),
        CloudProviderKind.OneDrive => Env(OneDriveCloudBackupProvider.ClientIdEnv),
        _ => null
    };

    public string? GetClientSecret(CloudProviderKind provider) => provider switch
    {
        CloudProviderKind.Dropbox => Env(DropboxCloudBackupProvider.AppSecretEnv),
        CloudProviderKind.GoogleDrive => Env(GoogleDriveCloudBackupProvider.ClientSecretEnv),
        _ => null
    };

    public string GetRedirectUri(CloudProviderKind provider) => provider switch
    {
        CloudProviderKind.Dropbox => "http://127.0.0.1:5180/oauth/dropbox",
        CloudProviderKind.GoogleDrive => "http://127.0.0.1:5180/oauth/google",
        CloudProviderKind.OneDrive => "http://127.0.0.1:5180/oauth/onedrive",
        _ => "http://127.0.0.1:5180/oauth"
    };

    public bool IsConfigured(CloudProviderKind provider) => !string.IsNullOrWhiteSpace(GetClientId(provider));

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
