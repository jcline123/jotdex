using Jotdex.Core.Integrations;
using Jotdex.Infrastructure.Integrations;
using Jotdex.Unit.Tests.CloudBackup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Unit.Tests.Integrations;

public class IntegrationTokenStoreTests : IDisposable
{
    private readonly string _root;
    private readonly IntegrationTokenStore _store;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-08T12:00:00Z"));

    public IntegrationTokenStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-int-tok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new IntegrationTokenStore(new TestDataRoot(_root), NullLogger<IntegrationTokenStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void Create_authenticate_revoke_and_expire()
    {
        var created = _store.Create(new IntegrationTokenCreateRequest
        {
            Name = "Bot",
            Scopes = IntegrationScopes.PresetCaptureAppend,
            AllowedFolderRoots = ["Inbox"],
            ExpirationDays = 90
        }, "vault-a", _time);
        Assert.True(created.Success);
        Assert.StartsWith("jdx_", created.PlaintextSecret);

        Assert.NotNull(_store.Authenticate(created.PlaintextSecret!, "vault-a", _time));
        Assert.Null(_store.Authenticate(created.PlaintextSecret!, "other-vault", _time));
        Assert.Null(_store.Authenticate("jdx_nope_secret", "vault-a", _time));

        Assert.True(_store.Revoke(created.TokenId!, _time));
        Assert.Null(_store.Authenticate(created.PlaintextSecret!, "vault-a", _time));

        var again = _store.Create(new IntegrationTokenCreateRequest
        {
            Name = "Short",
            Scopes = IntegrationScopes.PresetReadOnly,
            WholeVault = true,
            ExpirationDays = 1
        }, "vault-a", _time);
        Assert.True(again.Success);
        _time.Advance(TimeSpan.FromDays(2));
        Assert.Null(_store.Authenticate(again.PlaintextSecret!, "vault-a", _time));
    }

    [Fact]
    public void Corrupt_store_fail_closed()
    {
        var path = Path.Combine(_root, "integrations", "tokens.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        var store = new IntegrationTokenStore(new TestDataRoot(_root), NullLogger<IntegrationTokenStore>.Instance);
        Assert.ThrowsAny<Exception>(() => store.List());
    }

    [Fact]
    public void Requires_folders_or_whole_vault()
    {
        var bad = _store.Create(new IntegrationTokenCreateRequest
        {
            Name = "No folders",
            Scopes = IntegrationScopes.PresetReadOnly,
            WholeVault = false,
            AllowedFolderRoots = []
        }, "vault-a", _time);
        Assert.False(bad.Success);
    }

    [Fact]
    public void Never_expires_stays_valid_after_years()
    {
        var created = _store.Create(new IntegrationTokenCreateRequest
        {
            Name = "Forever Bot",
            Scopes = IntegrationScopes.PresetReadOnly,
            WholeVault = true,
            NeverExpires = true,
            ExpirationDays = 0
        }, "vault-a", _time);
        Assert.True(created.Success, created.Error);
        Assert.True(created.Token!.NeverExpires);
        _time.Advance(TimeSpan.FromDays(4000));
        Assert.NotNull(_store.Authenticate(created.PlaintextSecret!, "vault-a", _time));
        Assert.Contains(_store.List(), t => t.Id == created.TokenId && t.NeverExpires && t.Active);
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
