using Jotdex.Core.Auth;
using Jotdex.Core.Configuration;
using Jotdex.Infrastructure.Auth;
using Jotdex.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;

namespace Jotdex.Tests.Smoke;

public class LocalAuthServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DpapiSecretStore _secrets;
    private readonly LocalAuthService _auth;

    public LocalAuthServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var data = new TempDataRoot(_root);
        _secrets = new DpapiSecretStore(data, NullLogger<DpapiSecretStore>.Instance);
        _auth = new LocalAuthService(data, _secrets, NullLogger<LocalAuthService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void CreateAdmin_hashes_password_and_blocks_duplicate()
    {
        Assert.False(_auth.IsSetupComplete);
        var created = _auth.CreateAdmin("", "correct-horse-battery");
        Assert.True(created.Success);
        Assert.True(_auth.IsSetupComplete);
        Assert.Equal(ILocalAuthService.DefaultUsername, created.Username);

        var dup = _auth.CreateAdmin("", "correct-horse-battery");
        Assert.False(dup.Success);

        var bad = _auth.ValidateCredentials("", "wrong-password!!");
        Assert.False(bad.Success);

        var ok = _auth.ValidateCredentials("", "correct-horse-battery");
        Assert.True(ok.Success);
        Assert.Equal(ILocalAuthService.DefaultUsername, ok.Username);
    }

    [Fact]
    public void Lockout_after_repeated_failures()
    {
        Assert.True(_auth.CreateAdmin("", "correct-horse-battery").Success);
        for (var i = 0; i < 5; i++)
            Assert.False(_auth.ValidateCredentials("", "nope-nope-no").Success);

        var locked = _auth.ValidateCredentials("", "correct-horse-battery");
        Assert.False(locked.Success);
        Assert.True(locked.LockedOut);
    }

    [Fact]
    public void ChangePassword_requires_current()
    {
        Assert.True(_auth.CreateAdmin("", "correct-horse-battery").Success);
        var fail = _auth.ChangePassword("", "wrong-current!", "new-password-ok");
        Assert.False(fail.Success);
        var ok = _auth.ChangePassword("", "correct-horse-battery", "new-password-ok");
        Assert.True(ok.Success);
        Assert.True(_auth.ValidateCredentials("", "new-password-ok").Success);
        Assert.False(_auth.ValidateCredentials("", "correct-horse-battery").Success);
    }

    [Fact]
    public void RemovePassword_clears_protection()
    {
        Assert.True(_auth.CreateAdmin("", "correct-horse-battery").Success);
        Assert.False(_auth.RemovePassword("wrong").Success);
        Assert.True(_auth.RemovePassword("correct-horse-battery").Success);
        Assert.False(_auth.IsSetupComplete);
        Assert.False(_auth.ValidateCredentials("", "correct-horse-battery").Success);
    }

    [Fact]
    public void Totp_requires_code_after_enrollment()
    {
        Assert.True(_auth.CreateAdmin("", "correct-horse-battery").Success);
        var begin = _auth.BeginTotpEnrollment(ILocalAuthService.DefaultUsername);
        Assert.True(begin.Success);
        Assert.False(string.IsNullOrWhiteSpace(begin.ManualKey));

        var totp = new Totp(Base32Encoding.ToBytes(begin.ManualKey!));
        var code = totp.ComputeTotp();
        var confirm = _auth.ConfirmTotpEnrollment(ILocalAuthService.DefaultUsername, code);
        Assert.True(confirm.Success);
        Assert.NotNull(confirm.RecoveryCodes);
        Assert.NotEmpty(confirm.RecoveryCodes!);

        var needs = _auth.ValidateCredentials("", "correct-horse-battery");
        Assert.False(needs.Success);
        Assert.True(needs.RequiresTotp);

        var code2 = totp.ComputeTotp();
        var ok = _auth.ValidateCredentials("", "correct-horse-battery", code2);
        Assert.True(ok.Success);
    }

    [Fact]
    public void SecretStore_round_trips_and_portable_import()
    {
        _secrets.Set("notifications.smtp.password", "s3cret!");
        Assert.True(_secrets.Has("notifications.smtp.password"));
        Assert.True(_secrets.TryGet("notifications.smtp.password", out var v));
        Assert.Equal("s3cret!", v);

        var portable = _secrets.ExportPortable();
        Assert.Equal("s3cret!", portable["notifications.smtp.password"]);

        var portablePath = Path.Combine(_root, "secrets", "secrets-portable.json");
        Directory.CreateDirectory(Path.GetDirectoryName(portablePath)!);
        File.WriteAllText(portablePath, """{"kind":"jotdex-secrets-portable","secrets":{"notifications.telegram.botToken":"tok-123"}}""");
        var n = _secrets.ImportPortableFileIfPresent();
        Assert.True(n >= 1);
        Assert.False(File.Exists(portablePath));
        Assert.True(_secrets.TryGet("notifications.telegram.botToken", out var tok));
        Assert.Equal("tok-123", tok);
    }

    private sealed class TempDataRoot : IDataRootResolver
    {
        private readonly string _root;
        public TempDataRoot(string root) => _root = root;
        public string ResolveDataRoot() => _root;
        public string? ResolveVaultPathOrNull() => null;
        public bool IsVaultConfigured => false;
    }
}
