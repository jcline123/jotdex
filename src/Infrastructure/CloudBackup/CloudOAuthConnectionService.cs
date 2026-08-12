using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup.Providers;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudOAuthConnectionService : ICloudOAuthConnectionService
{
    private readonly ICloudCredentialStore _credentials;
    private readonly ICloudBackupSettingsService _settings;
    private readonly ICloudBackupStateStore _state;
    private readonly IEnumerable<ICloudBackupProvider> _providers;
    private readonly ICloudOAuthClientConfig _oauthConfig;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudOAuthConnectionService> _logger;
    private readonly ConcurrentDictionary<string, OAuthAttemptState> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public CloudOAuthConnectionService(
        ICloudCredentialStore credentials,
        ICloudBackupSettingsService settings,
        ICloudBackupStateStore state,
        IEnumerable<ICloudBackupProvider> providers,
        ICloudOAuthClientConfig oauthConfig,
        IHttpClientFactory httpClientFactory,
        ILogger<CloudOAuthConnectionService> logger)
    {
        _credentials = credentials;
        _settings = settings;
        _state = state;
        _providers = providers;
        _oauthConfig = oauthConfig;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<CloudOAuthAttempt> BeginConnectAsync(CloudProviderKind provider, CancellationToken cancellationToken)
    {
        var adapter = _providers.FirstOrDefault(p => p.Kind == provider);
        if (adapter is null || !_oauthConfig.IsConfigured(provider))
        {
            return Task.FromResult(new CloudOAuthAttempt
            {
                AttemptId = Guid.NewGuid().ToString("N"),
                Provider = provider,
                CreatedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                Completed = true,
                Success = false,
                Error = "ConfigurationUnavailable",
                AuthorizeUrl = null
            });
        }

        var attemptId = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = CreatePkce();
        var redirectUri = _oauthConfig.GetRedirectUri(provider);
        var authorizeUrl = BuildAuthorizeUrl(provider, attemptId, challenge, redirectUri);
        var publicAttempt = new CloudOAuthAttempt
        {
            AttemptId = attemptId,
            Provider = provider,
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(15),
            Completed = false,
            Success = false,
            AuthorizeUrl = authorizeUrl
        };
        _attempts[Key(provider, attemptId)] = new OAuthAttemptState
        {
            Public = publicAttempt,
            CodeVerifier = verifier,
            CodeChallenge = challenge,
            RedirectUri = redirectUri
        };

        var state = _state.Get();
        var pst = state.Providers.First(p => p.Provider == provider);
        pst.ConnectionState = CloudConnectionState.Connecting;
        _state.Save(state);

        return Task.FromResult(Clone(publicAttempt));
    }

    public CloudOAuthAttempt? GetAttempt(CloudProviderKind provider, string attemptId)
    {
        _attempts.TryGetValue(Key(provider, attemptId), out var attempt);
        return attempt is null ? null : Clone(attempt.Public);
    }

    public async Task<CloudOAuthAttempt> CompleteAsync(
        CloudProviderKind provider,
        string attemptId,
        string? authorizationCode,
        CancellationToken cancellationToken)
    {
        if (!_attempts.TryGetValue(Key(provider, attemptId), out var attempt))
        {
            return new CloudOAuthAttempt
            {
                AttemptId = attemptId,
                Provider = provider,
                CreatedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow,
                Completed = true,
                Success = false,
                Error = "Unknown or expired attempt."
            };
        }

        if (attempt.Public.ExpiresUtc < DateTimeOffset.UtcNow)
        {
            attempt.Public.Completed = true;
            attempt.Public.Success = false;
            attempt.Public.Error = "Attempt expired.";
            return Clone(attempt.Public);
        }

        if (attempt.Public.Completed)
            return Clone(attempt.Public);

        var adapter = _providers.FirstOrDefault(p => p.Kind == provider);
        if (adapter is null || !_oauthConfig.IsConfigured(provider))
        {
            attempt.Public.Completed = true;
            attempt.Public.Success = false;
            attempt.Public.Error = "ConfigurationUnavailable";
            return Clone(attempt.Public);
        }

        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            attempt.Public.Completed = true;
            attempt.Public.Success = false;
            attempt.Public.Error = "Authorization code missing.";
            MarkReconnect(provider, CloudBackupFailureCode.AuthorizationDenied, attempt.Public.Error);
            return Clone(attempt.Public);
        }

        try
        {
            var tokens = await ExchangeAuthorizationCodeAsync(
                provider,
                authorizationCode.Trim(),
                attempt.CodeVerifier,
                attempt.RedirectUri,
                cancellationToken).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new
            {
                refresh_token = tokens.RefreshToken,
                access_token = tokens.AccessToken,
                expires_utc = tokens.ExpiresUtc
            });

            _credentials.Set(provider, new CloudCredentialEnvelope
            {
                Provider = provider,
                AccountId = "pending",
                AccountDisplayName = null,
                AccountEmail = null,
                ProtectedPayload = payload,
                UpdatedUtc = DateTimeOffset.UtcNow
            });

            CloudProviderAccount account;
            try
            {
                account = await adapter.GetAccountAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cloud OAuth token exchange succeeded but account lookup failed for {Provider}", provider);
                account = new CloudProviderAccount
                {
                    AccountId = "connected",
                    DisplayName = provider + " account",
                    Email = null
                };
            }

            _credentials.Set(provider, new CloudCredentialEnvelope
            {
                Provider = provider,
                AccountId = account.AccountId,
                AccountDisplayName = account.DisplayName,
                AccountEmail = account.Email,
                ProtectedPayload = payload,
                UpdatedUtc = DateTimeOffset.UtcNow
            });

            var settings = _settings.Get();
            var cfg = settings.Providers.First(p => p.Provider == provider);
            cfg.Enabled = true;
            cfg.AccountId = account.AccountId;
            cfg.AccountDisplayName = account.DisplayName;
            cfg.AccountEmail = account.Email;
            _settings.Save(settings);

            var state = _state.Get();
            var pst = state.Providers.First(p => p.Provider == provider);
            pst.ConnectionState = CloudConnectionState.Connected;
            pst.LastFailureCode = CloudBackupFailureCode.None;
            pst.LastFailureMessage = null;
            pst.Health = CloudBackupHealthLevel.Pending;
            _state.Save(state);

            attempt.Public.Completed = true;
            attempt.Public.Success = true;
            attempt.Public.AccountDisplayName = account.DisplayName;
            attempt.Public.AccountEmail = account.Email;
            attempt.Public.Error = null;
            _logger.LogInformation("Cloud OAuth complete for {Provider} ({Account})", provider, account.DisplayName ?? account.AccountId);
            return Clone(attempt.Public);
        }
        catch (CloudBackupProviderException ex)
        {
            attempt.Public.Completed = true;
            attempt.Public.Success = false;
            attempt.Public.Error = ex.Message;
            MarkReconnect(provider, ex.Code, ex.Message);
            return Clone(attempt.Public);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud OAuth token exchange failed for {Provider}", provider);
            attempt.Public.Completed = true;
            attempt.Public.Success = false;
            attempt.Public.Error = "Token exchange failed.";
            MarkReconnect(provider, CloudBackupFailureCode.AuthenticationRequired, attempt.Public.Error);
            return Clone(attempt.Public);
        }
    }

    public Task DisconnectAsync(CloudProviderKind provider, CancellationToken cancellationToken)
    {
        _credentials.Remove(provider);
        var settings = _settings.Get();
        var cfg = settings.Providers.FirstOrDefault(p => p.Provider == provider);
        if (cfg is not null)
        {
            cfg.Enabled = false;
            cfg.AccountId = null;
            cfg.AccountDisplayName = null;
            cfg.AccountEmail = null;
            cfg.RemoteRootId = null;
            cfg.RemoteRootDisplayPath = null;
            _settings.Save(settings);
        }

        var state = _state.Get();
        var pst = state.Providers.First(p => p.Provider == provider);
        pst.ConnectionState = CloudConnectionState.Disconnected;
        pst.Health = CloudBackupHealthLevel.NotConfigured;
        _state.Save(state);
        return Task.CompletedTask;
    }

    private void MarkReconnect(CloudProviderKind provider, CloudBackupFailureCode code, string? message)
    {
        var state = _state.Get();
        var pst = state.Providers.First(p => p.Provider == provider);
        pst.ConnectionState = CloudConnectionState.ReconnectRequired;
        pst.LastFailureCode = code;
        pst.LastFailureMessage = message;
        pst.Health = CloudBackupHealthLevel.Error;
        _state.Save(state);
    }

    private async Task<TokenExchangeResult> ExchangeAuthorizationCodeAsync(
        CloudProviderKind provider,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken ct)
    {
        return provider switch
        {
            CloudProviderKind.Dropbox => await ExchangeDropboxAsync(code, codeVerifier, redirectUri, ct).ConfigureAwait(false),
            CloudProviderKind.GoogleDrive => await ExchangeGoogleAsync(code, codeVerifier, redirectUri, ct).ConfigureAwait(false),
            CloudProviderKind.OneDrive => await ExchangeOneDriveAsync(code, codeVerifier, redirectUri, ct).ConfigureAwait(false),
            _ => throw new CloudBackupProviderException(CloudBackupFailureCode.ProviderUnavailable, "Unknown provider.")
        };
    }

    private async Task<TokenExchangeResult> ExchangeDropboxAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var appKey = _oauthConfig.GetClientId(CloudProviderKind.Dropbox)
                     ?? throw CloudBackupProviderException.ConfigurationMissing(CloudProviderKind.Dropbox);
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = appKey,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri
        };
        var secret = _oauthConfig.GetClientSecret(CloudProviderKind.Dropbox);
        if (!string.IsNullOrWhiteSpace(secret))
            fields["client_secret"] = secret;

        var client = _httpClientFactory.CreateClient("cloud-dropbox");
        using var content = new FormUrlEncodedContent(fields);
        using var res = await client.PostAsync("https://api.dropboxapi.com/oauth2/token", content, ct).ConfigureAwait(false);
        return await ParseTokenResponseAsync(res, CloudProviderKind.Dropbox, requireRefresh: true, ct).ConfigureAwait(false);
    }

    private async Task<TokenExchangeResult> ExchangeGoogleAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var clientId = _oauthConfig.GetClientId(CloudProviderKind.GoogleDrive)
                       ?? throw CloudBackupProviderException.ConfigurationMissing(CloudProviderKind.GoogleDrive);
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri
        };
        var secret = _oauthConfig.GetClientSecret(CloudProviderKind.GoogleDrive);
        if (!string.IsNullOrWhiteSpace(secret))
            fields["client_secret"] = secret;

        var client = _httpClientFactory.CreateClient("cloud-google");
        using var content = new FormUrlEncodedContent(fields);
        using var res = await client.PostAsync("https://oauth2.googleapis.com/token", content, ct).ConfigureAwait(false);
        return await ParseTokenResponseAsync(res, CloudProviderKind.GoogleDrive, requireRefresh: true, ct).ConfigureAwait(false);
    }

    private async Task<TokenExchangeResult> ExchangeOneDriveAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var clientId = _oauthConfig.GetClientId(CloudProviderKind.OneDrive)
                       ?? throw CloudBackupProviderException.ConfigurationMissing(CloudProviderKind.OneDrive);
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = "offline_access Files.ReadWrite.AppFolder User.Read"
        };

        var client = _httpClientFactory.CreateClient("cloud-onedrive");
        using var content = new FormUrlEncodedContent(fields);
        using var res = await client.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token", content, ct).ConfigureAwait(false);
        return await ParseTokenResponseAsync(res, CloudProviderKind.OneDrive, requireRefresh: true, ct).ConfigureAwait(false);
    }

    private static async Task<TokenExchangeResult> ParseTokenResponseAsync(
        HttpResponseMessage res,
        CloudProviderKind provider,
        bool requireRefresh,
        CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            throw new CloudBackupProviderException(
                CloudBackupFailureCode.AuthenticationRequired,
                $"{provider} token exchange failed ({(int)res.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString()
                     ?? throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired, "Missing access_token.");
        var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (requireRefresh && string.IsNullOrWhiteSpace(refresh))
            throw new CloudBackupProviderException(CloudBackupFailureCode.AuthenticationRequired,
                $"{provider} did not return a refresh_token (offline access required).");
        var expiresIn = root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600;
        return new TokenExchangeResult
        {
            AccessToken = access,
            RefreshToken = refresh ?? "",
            ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
        };
    }

    private string BuildAuthorizeUrl(
        CloudProviderKind provider,
        string attemptId,
        string codeChallenge,
        string redirectUri)
    {
        var clientId = _oauthConfig.GetClientId(provider) ?? "";
        return provider switch
        {
            CloudProviderKind.Dropbox =>
                "https://www.dropbox.com/oauth2/authorize?client_id="
                + Uri.EscapeDataString(clientId)
                + "&response_type=code&token_access_type=offline"
                + "&code_challenge_method=S256&code_challenge=" + Uri.EscapeDataString(codeChallenge)
                + "&state=" + Uri.EscapeDataString(attemptId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirectUri),
            CloudProviderKind.GoogleDrive =>
                "https://accounts.google.com/o/oauth2/v2/auth?client_id="
                + Uri.EscapeDataString(clientId)
                + "&response_type=code&access_type=offline&prompt=consent&scope="
                + Uri.EscapeDataString("https://www.googleapis.com/auth/drive.file")
                + "&code_challenge_method=S256&code_challenge=" + Uri.EscapeDataString(codeChallenge)
                + "&state=" + Uri.EscapeDataString(attemptId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirectUri),
            CloudProviderKind.OneDrive =>
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?client_id="
                + Uri.EscapeDataString(clientId)
                + "&response_type=code&response_mode=query&scope="
                + Uri.EscapeDataString("offline_access Files.ReadWrite.AppFolder User.Read")
                + "&code_challenge_method=S256&code_challenge=" + Uri.EscapeDataString(codeChallenge)
                + "&state=" + Uri.EscapeDataString(attemptId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirectUri),
            _ => ""
        };
    }

    internal static (string Verifier, string Challenge) CreatePkce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(bytes);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return (verifier, Base64UrlEncode(hash));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Key(CloudProviderKind provider, string attemptId) =>
        provider + ":" + attemptId;

    private static CloudOAuthAttempt Clone(CloudOAuthAttempt a) =>
        new()
        {
            AttemptId = a.AttemptId,
            Provider = a.Provider,
            CreatedUtc = a.CreatedUtc,
            ExpiresUtc = a.ExpiresUtc,
            Completed = a.Completed,
            Success = a.Success,
            Error = a.Error,
            AuthorizeUrl = a.AuthorizeUrl,
            AccountDisplayName = a.AccountDisplayName,
            AccountEmail = a.AccountEmail
        };

    private sealed class OAuthAttemptState
    {
        public required CloudOAuthAttempt Public { get; init; }
        public required string CodeVerifier { get; init; }
        public required string CodeChallenge { get; init; }
        public required string RedirectUri { get; init; }
    }

    private sealed class TokenExchangeResult
    {
        public required string AccessToken { get; init; }
        public required string RefreshToken { get; init; }
        public required DateTimeOffset ExpiresUtc { get; init; }
    }
}
