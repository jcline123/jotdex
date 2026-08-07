namespace Jotdex.Core.Auth;

public sealed class AuthStatus
{
    public required bool SetupComplete { get; init; }
    public required bool Authenticated { get; init; }
    public required bool AuthRequired { get; init; }
    public string? Username { get; init; }
    public string? DisplayName { get; init; }
}

public sealed class AuthResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? Username { get; init; }
    public bool LockedOut { get; init; }
    public int? RetryAfterSeconds { get; init; }
}

public interface ILocalAuthService
{
    bool IsSetupComplete { get; }
    AuthStatus GetStatus(string? currentUsername);
    AuthResult CreateAdmin(string username, string password, string? displayName = null);
    AuthResult ValidateCredentials(string username, string password);
    AuthResult ChangePassword(string username, string currentPassword, string newPassword);
    void RecordFailedLogin(string username);
    void RecordSuccessfulLogin(string username);
}
