using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.Auth;
using Jotdex.Core.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<LocalAuthService> _logger;
    private readonly PasswordHasher<AuthUserRecord> _hasher = new();
    private readonly object _gate = new();

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int MinPasswordLength = 10;

    public LocalAuthService(IDataRootResolver dataRoot, ILogger<LocalAuthService> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public bool IsSetupComplete
    {
        get
        {
            lock (_gate)
            {
                var store = LoadUnlocked();
                return store.Users.Count > 0;
            }
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

            return new AuthStatus
            {
                SetupComplete = store.Users.Count > 0,
                Authenticated = user is not null,
                AuthRequired = store.Users.Count > 0,
                Username = user?.Username,
                DisplayName = user?.DisplayName
            };
        }
    }

    public AuthResult CreateAdmin(string username, string password, string? displayName = null)
    {
        username = (username ?? "").Trim();
        if (username.Length < 2)
            return Fail("Username must be at least 2 characters.");
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return Fail($"Password must be at least {MinPasswordLength} characters.");

        lock (_gate)
        {
            var store = LoadUnlocked();
            if (store.Users.Count > 0)
                return Fail("Administrator already exists.");

            var record = new AuthUserRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
                Role = "admin",
                CreatedUtc = DateTimeOffset.UtcNow
            };
            record.PasswordHash = _hasher.HashPassword(record, password);
            store.Users.Add(record);
            SaveUnlocked(store);
            _logger.LogInformation("Administrator account created");
            return new AuthResult { Success = true, Username = record.Username };
        }
    }

    public AuthResult ValidateCredentials(string username, string password)
    {
        username = (username ?? "").Trim();
        lock (_gate)
        {
            var store = LoadUnlocked();
            var user = store.Users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (user is null)
                return Fail("Invalid username or password.");

            if (user.LockoutUntilUtc is { } until && until > DateTimeOffset.UtcNow)
            {
                var seconds = (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds);
                return new AuthResult
                {
                    Success = false,
                    LockedOut = true,
                    RetryAfterSeconds = Math.Max(1, seconds),
                    Error = "Account temporarily locked. Try again later."
                };
            }

            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
            if (result == PasswordVerificationResult.Failed)
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
                        Error = "Too many failed attempts. Account locked temporarily."
                    };
                }

                SaveUnlocked(store);
                return Fail("Invalid username or password.");
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
            var user = store.Users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (user is null)
                return Fail("User not found.");

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

    public void RecordFailedLogin(string username) { /* handled in ValidateCredentials */ }

    public void RecordSuccessfulLogin(string username) { /* handled in ValidateCredentials */ }

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
    }
}
