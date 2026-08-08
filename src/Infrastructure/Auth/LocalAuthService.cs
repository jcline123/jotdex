using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.Auth;
using Jotdex.Core.Configuration;
using Jotdex.Core.Notifications;
using Jotdex.Core.Secrets;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace Jotdex.Infrastructure.Auth;

public sealed class LocalAuthService : ILocalAuthService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ISecretStore _secrets;
    private readonly ILogger<LocalAuthService> _logger;
    private readonly PasswordHasher<AuthUserRecord> _hasher = new();
    private readonly object _gate = new();
    private string? _pendingTotpSecret;

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int MinPasswordLength = 6;
    private const int RecoveryCodeCount = 8;

    public LocalAuthService(IDataRootResolver dataRoot, ISecretStore secrets, ILogger<LocalAuthService> logger)
    {
        _dataRoot = dataRoot;
        _secrets = secrets;
        _logger = logger;
    }

    public bool IsSetupComplete
    {
        get
        {
            lock (_gate)
            {
                return LoadUnlocked().Users.Count > 0;
            }
        }
    }

    public bool IsTotpEnabled()
    {
        lock (_gate)
        {
            var user = LoadUnlocked().Users.FirstOrDefault();
            return user?.TotpEnabled == true;
        }
    }

    public AuthStatus GetStatus(string? currentUsername)
    {
        lock (_gate)
        {
            var store = LoadUnlocked();
            var user = string.IsNullOrWhiteSpace(currentUsername)
                ? null
                : store.Users.FirstOrDefault(u =>
                    string.Equals(u.Username, currentUsername, StringComparison.OrdinalIgnoreCase));

            var any = store.Users.FirstOrDefault();
            return new AuthStatus
            {
                SetupComplete = store.Users.Count > 0,
                Authenticated = user is not null,
                AuthRequired = store.Users.Count > 0,
                Username = user?.Username,
                DisplayName = user?.DisplayName,
                TotpEnabled = any?.TotpEnabled == true
            };
        }
    }

    public AuthResult CreateAdmin(string username, string password, string? displayName = null)
    {
        username = string.IsNullOrWhiteSpace(username) ? ILocalAuthService.DefaultUsername : username.Trim();
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return Fail($"Password must be at least {MinPasswordLength} characters.");

        lock (_gate)
        {
            var store = LoadUnlocked();
            if (store.Users.Count > 0)
                return Fail("A password is already set.");

            var record = new AuthUserRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Admin" : displayName.Trim(),
                Role = "admin",
                CreatedUtc = DateTimeOffset.UtcNow
            };
            record.PasswordHash = _hasher.HashPassword(record, password);
            store.Users.Add(record);
            SaveUnlocked(store);
            _logger.LogInformation("Password protection enabled");
            return new AuthResult { Success = true, Username = record.Username };
        }
    }

    public AuthResult ValidateCredentials(string username, string password, string? totpOrRecoveryCode = null)
    {
        lock (_gate)
        {
            var store = LoadUnlocked();
            var user = ResolveUser(store, username);
            if (user is null)
                return Fail("Invalid password.");

            if (user.LockoutUntilUtc is { } until && until > DateTimeOffset.UtcNow)
            {
                var seconds = (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds);
                return new AuthResult
                {
                    Success = false,
                    LockedOut = true,
                    RetryAfterSeconds = Math.Max(1, seconds),
                    Error = "Temporarily locked. Try again later."
                };
            }

            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
            if (result == PasswordVerificationResult.Failed)
                return FailAfterAttempt(store, user, "Invalid password.");

            if (user.TotpEnabled)
            {
                if (string.IsNullOrWhiteSpace(totpOrRecoveryCode))
                {
                    return new AuthResult
                    {
                        Success = false,
                        RequiresTotp = true,
                        Username = user.Username,
                        Error = "Enter your authenticator code."
                    };
                }

                if (!VerifyTotpOrRecovery(store, user, totpOrRecoveryCode.Trim()))
                    return FailAfterAttempt(store, user, "Invalid authenticator or recovery code.");
            }

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                user.PasswordHash = _hasher.HashPassword(user, password!);

            user.FailedAttempts = 0;
            user.LockoutUntilUtc = null;
            user.LastLoginUtc = DateTimeOffset.UtcNow;
            SaveUnlocked(store);
            return new AuthResult { Success = true, Username = user.Username };
        }
    }

    public AuthResult ChangePassword(string username, string currentPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
            return Fail($"New password must be at least {MinPasswordLength} characters.");

        lock (_gate)
        {
            var store = LoadUnlocked();
            var user = ResolveUser(store, username);
            if (user is null)
                return Fail("No password is set.");

            var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? "");
            if (verify == PasswordVerificationResult.Failed)
                return Fail("Current password is incorrect.");

            user.PasswordHash = _hasher.HashPassword(user, newPassword);
            user.FailedAttempts = 0;
            user.LockoutUntilUtc = null;
            SaveUnlocked(store);
            return new AuthResult { Success = true, Username = user.Username };
        }
    }

    public AuthResult RemovePassword(string currentPassword)
    {
        lock (_gate)
        {
            var store = LoadUnlocked();
            if (store.Users.Count == 0)
                return Fail("No password is set.");

            var user = store.Users[0];
            var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? "");
            if (verify == PasswordVerificationResult.Failed)
                return Fail("Current password is incorrect.");

            store.Users.Clear();
            SaveUnlocked(store);
            _secrets.Remove(SecretKeys.TotpSecret);
            _pendingTotpSecret = null;
            _logger.LogInformation("Password protection removed");
            return new AuthResult { Success = true };
        }
    }

    public TotpBeginResult BeginTotpEnrollment(string username)
    {
        lock (_gate)
        {
            var store = LoadUnlocked();
            var user = ResolveUser(store, username);
            if (user is null)
                return new TotpBeginResult { Success = false, Error = "Set a password first." };
            if (user.TotpEnabled)
                return new TotpBeginResult { Success = false, Error = "Authenticator is already enabled. Disable it first to re-enroll." };

            var key = KeyGeneration.GenerateRandomKey(20);
            var manual = Base32Encoding.ToString(key);
            _pendingTotpSecret = manual;
            var label = Uri.EscapeDataString($"Jotdex:{user.Username}");
            var issuer = Uri.EscapeDataString("Jotdex");
            var uri = $"otpauth://totp/{label}?secret={manual}&issuer={issuer}&digits=6&period=30";
            return new TotpBeginResult { Success = true, ManualKey = manual, OtpAuthUri = uri };
        }
    }

    public TotpConfirmResult ConfirmTotpEnrollment(string username, string code)
    {
        lock (_gate)
        {
            var store = LoadUnlocked();
            var user = ResolveUser(store, username);
            if (user is null)
                return new TotpConfirmResult { Success = false, Error = "Set a password first." };
            if (string.IsNullOrWhiteSpace(_pendingTotpSecret))
                return new TotpConfirmResult { Success = false, Error = "Start enrollment first." };

            if (!VerifyCode(_pendingTotpSecret, code))
                return new TotpConfirmResult { Success = false, Error = "Invalid code. Try again." };

            _secrets.Set(SecretKeys.TotpSecret, _pendingTotpSecret);
            var recovery = GenerateRecoveryCodes();
            user.TotpEnabled = true;
            user.RecoveryCodeHashes = recovery.Select(HashRecovery).ToList();
            _pendingTotpSecret = null;
            SaveUnlocked(store);
            _logger.LogInformation("TOTP enabled for {User}", user.Username);
            return new TotpConfirmResult { Success = true, RecoveryCodes = recovery };
        }
    }

    public AuthResult DisableTotp(string username, string password, string? totpOrRecoveryCode = null)
    {
        lock (_gate)
        {
            var store = LoadUnlocked();
            var user = ResolveUser(store, username);
            if (user is null)
                return Fail("No password is set.");

            var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
            if (verify == PasswordVerificationResult.Failed)
                return Fail("Current password is incorrect.");

            if (user.TotpEnabled)
            {
                if (string.IsNullOrWhiteSpace(totpOrRecoveryCode) ||
                    !VerifyTotpOrRecovery(store, user, totpOrRecoveryCode.Trim()))
                    return Fail("Authenticator or recovery code required to disable.");
            }

            user.TotpEnabled = false;
            user.RecoveryCodeHashes = [];
            _secrets.Remove(SecretKeys.TotpSecret);
            _pendingTotpSecret = null;
            SaveUnlocked(store);
            _logger.LogInformation("TOTP disabled for {User}", user.Username);
            return new AuthResult { Success = true, Username = user.Username };
        }
    }

    public void RecordFailedLogin(string username) { /* handled in ValidateCredentials */ }

    public void RecordSuccessfulLogin(string username) { /* handled in ValidateCredentials */ }

    private AuthResult FailAfterAttempt(AuthStore store, AuthUserRecord user, string message)
    {
        user.FailedAttempts++;
        if (user.FailedAttempts >= MaxFailedAttempts)
        {
            user.LockoutUntilUtc = DateTimeOffset.UtcNow.Add(LockoutDuration);
            user.FailedAttempts = 0;
            SaveUnlocked(store);
            return new AuthResult
            {
                Success = false,
                LockedOut = true,
                RetryAfterSeconds = (int)LockoutDuration.TotalSeconds,
                Error = "Too many failed attempts. Locked temporarily."
            };
        }

        SaveUnlocked(store);
        return Fail(message);
    }

    private bool VerifyTotpOrRecovery(AuthStore store, AuthUserRecord user, string code)
    {
        if (_secrets.TryGet(SecretKeys.TotpSecret, out var secret) && !string.IsNullOrEmpty(secret) &&
            VerifyCode(secret, code))
            return true;

        var hash = HashRecovery(code);
        var idx = user.RecoveryCodeHashes.FindIndex(h =>
            string.Equals(h, hash, StringComparison.Ordinal));
        if (idx < 0) return false;
        user.RecoveryCodeHashes.RemoveAt(idx);
        SaveUnlocked(store);
        _logger.LogInformation("Recovery code used for {User}; {Left} remaining", user.Username, user.RecoveryCodeHashes.Count);
        return true;
    }

    private static bool VerifyCode(string base32Secret, string code)
    {
        try
        {
            var cleaned = new string((code ?? "").Where(char.IsDigit).ToArray());
            if (cleaned.Length != 6) return false;
            var key = Base32Encoding.ToBytes(base32Secret.Replace(" ", ""));
            var totp = new Totp(key);
            return totp.VerifyTotp(cleaned, out _, new VerificationWindow(1, 1));
        }
        catch
        {
            return false;
        }
    }

    private static List<string> GenerateRecoveryCodes()
    {
        var list = new List<string>(RecoveryCodeCount);
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(5);
            list.Add(Convert.ToHexString(bytes).ToLowerInvariant());
        }
        return list;
    }

    private static string HashRecovery(string code)
    {
        var normalized = (code ?? "").Trim().ToLowerInvariant().Replace(" ", "");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("jotdex-recovery:" + normalized));
        return Convert.ToHexString(hash);
    }

    private static AuthUserRecord? ResolveUser(AuthStore store, string? username)
    {
        if (store.Users.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(username))
            return store.Users.Count == 1
                ? store.Users[0]
                : store.Users.FirstOrDefault(u =>
                    string.Equals(u.Username, ILocalAuthService.DefaultUsername, StringComparison.OrdinalIgnoreCase));
        return store.Users.FirstOrDefault(u =>
            string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private AuthStore LoadUnlocked()
    {
        var path = StorePath();
        if (!File.Exists(path))
            return new AuthStore();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AuthStore>(json, JsonOpts) ?? new AuthStore();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read auth store; treating as empty");
            return new AuthStore();
        }
    }

    private void SaveUnlocked(AuthStore store)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        var json = JsonSerializer.Serialize(store, JsonOpts);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private string StorePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "auth", "users.json");

    private static AuthResult Fail(string error) => new() { Success = false, Error = error };

    private sealed class AuthStore
    {
        public List<AuthUserRecord> Users { get; set; } = [];
    }

    private sealed class AuthUserRecord
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Role { get; set; } = "admin";
        public string PasswordHash { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset? LastLoginUtc { get; set; }
        public int FailedAttempts { get; set; }
        public DateTimeOffset? LockoutUntilUtc { get; set; }
        public bool TotpEnabled { get; set; }
        public List<string> RecoveryCodeHashes { get; set; } = [];
    }
}
