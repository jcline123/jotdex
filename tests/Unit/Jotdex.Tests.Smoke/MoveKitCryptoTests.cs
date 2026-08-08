using Jotdex.Core.Configuration;
using Jotdex.Core.Secrets;
using Jotdex.Infrastructure.Maintenance;
using Jotdex.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Tests.Smoke;

public class MoveKitCryptoTests : IDisposable
{
    private readonly string _root;

    public MoveKitCryptoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-kitcrypt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        var data = new TempRoot(_root);
        var secrets = new DpapiSecretStore(data, NullLogger<DpapiSecretStore>.Instance);
        var probe = new FixedProbe(true);
        var crypto = new MoveKitCryptoService(data, secrets, probe, NullLogger<MoveKitCryptoService>.Instance);
        crypto.OnPasswordSet("correct-horse-battery");

        var zip = Path.Combine(_root, "sample.zip");
        File.WriteAllText(zip, "hello-move-kit");
        var enc = crypto.EncryptZipFile(zip);
        Assert.EndsWith(".jotdexkit", enc, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(zip));

        var outZip = Path.Combine(_root, "out.zip");
        crypto.DecryptToZip(enc, "correct-horse-battery", outZip);
        Assert.Equal("hello-move-kit", File.ReadAllText(outZip));
    }

    private sealed class TempRoot(string root) : IDataRootResolver
    {
        public string ResolveDataRoot() => root;
        public string? ResolveVaultPathOrNull() => null;
        public bool IsVaultConfigured => false;
    }

    private sealed class FixedProbe(bool set) : ILocalAuthProbe
    {
        public bool IsPasswordSet => set;
    }
}
