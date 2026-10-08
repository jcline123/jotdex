using Jotdex.Core.Integrations;
using Jotdex.Infrastructure.Integrations;

namespace Jotdex.Unit.Tests.Integrations;

public class IntegrationFolderAccessTests
{
    private readonly IntegrationFolderAccess _acl = new();

    private static IntegrationTokenRecord Token(params string[] folders) => new()
    {
        Id = "t1",
        Name = "Test",
        SecretVerifierSha256 = "00",
        Scopes = IntegrationScopes.PresetReadOnly,
        AllowedFolderRoots = folders,
        WholeVault = false,
        VaultId = "vault-1",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
    };

    [Fact]
    public void Allows_folder_and_descendants_not_siblings()
    {
        var t = Token("Inbox/Bots");
        Assert.True(_acl.AllowsFolder(t, "Inbox/Bots"));
        Assert.True(_acl.AllowsFolder(t, "Inbox/Bots/Sub"));
        Assert.True(_acl.AllowsNotePath(t, "Inbox/Bots/Hello.md"));
        Assert.False(_acl.AllowsFolder(t, "Inbox"));
        Assert.False(_acl.AllowsFolder(t, "Inbox/Other"));
        Assert.False(_acl.AllowsNotePath(t, "Inbox/Other.md"));
        Assert.False(_acl.AllowsNotePath(t, "Inbox/BotsExtra/x.md")); // sibling-prefix trap
    }

    [Fact]
    public void Rejects_traversal_and_absolute_paths()
    {
        var t = Token("Inbox");
        Assert.False(_acl.AllowsNotePath(t, "../Secrets.md"));
        Assert.False(_acl.AllowsNotePath(t, "Inbox/../../Outside.md"));
        Assert.False(_acl.AllowsNotePath(t, "C:/Vault/Inbox/x.md"));
    }

    [Fact]
    public void Whole_vault_allows_any_relative_path()
    {
        var t = new IntegrationTokenRecord
        {
            Id = "t1",
            Name = "Test",
            SecretVerifierSha256 = "00",
            Scopes = IntegrationScopes.PresetReadOnly,
            AllowedFolderRoots = [],
            WholeVault = true,
            VaultId = "vault-1",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        Assert.True(_acl.AllowsNotePath(t, "Anywhere/Note.md"));
        Assert.True(_acl.AllowsFolder(t, ""));
    }
}
