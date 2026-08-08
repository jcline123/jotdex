using Jotdex.Core.Auth;
using Jotdex.Core.Configuration;
using Jotdex.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Tests.Smoke;

public class LocalAuthServiceTests : IDisposable
{
    private readonly string _root;
    private readonly LocalAuthService _auth;

    public LocalAuthServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _auth = new LocalAuthService(new TempDataRoot(_root), NullLogger<LocalAuthService>.Instance);
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

    private sealed class TempDataRoot : IDataRootResolver
    {
        private readonly string _root;
        public TempDataRoot(string root) => _root = root;
        public string ResolveDataRoot() => _root;
        public string? ResolveVaultPathOrNull() => null;
        public bool IsVaultConfigured => false;
    }
}
