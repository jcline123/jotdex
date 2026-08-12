using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Secrets;
using Jotdex.Infrastructure.Maintenance;
using Jotdex.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Unit.Tests.CloudBackup;

public class CloudBackupMoveKitCryptoTests : IDisposable
{
    private readonly string _root;

    public CloudBackupMoveKitCryptoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-kitcrypt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void Encrypt_JDXK2_then_decrypt_round_trips()
    {
        var data = new TestDataRoot(_root);
        var secrets = new DpapiSecretStore(data, NullLogger<DpapiSecretStore>.Instance);
        var crypto = new MoveKitCryptoService(data, secrets, new FixedProbe(true), NullLogger<MoveKitCryptoService>.Instance);
        crypto.OnPasswordSet("correct-horse-battery");

        var zip = Path.Combine(_root, "sample.zip");
        File.WriteAllText(zip, "hello-move-kit-jdxk2");
        var enc = crypto.EncryptZipFile(zip);
        Assert.EndsWith(".jotdexkit", enc, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(zip));

        using (var fs = File.OpenRead(enc))
        {
            var magic = new byte[5];
            Assert.Equal(5, fs.Read(magic));
            Assert.Equal("JDXK2", Encoding.ASCII.GetString(magic));
        }

        var outZip = Path.Combine(_root, "out.zip");
        crypto.DecryptToZip(enc, "correct-horse-battery", outZip);
        Assert.Equal("hello-move-kit-jdxk2", File.ReadAllText(outZip));
    }

    [Fact]
    public void Fabricated_JDXK1_still_decrypts()
    {
        var data = new TestDataRoot(_root);
        var secrets = new DpapiSecretStore(data, NullLogger<DpapiSecretStore>.Instance);
        var crypto = new MoveKitCryptoService(data, secrets, new FixedProbe(true), NullLogger<MoveKitCryptoService>.Instance);
        const string password = "legacy-kit-password";
        crypto.OnPasswordSet(password);

        Assert.True(secrets.TryGet("moveKit.aesKey", out var keyB64) && !string.IsNullOrEmpty(keyB64));
        var aesKey = Convert.FromBase64String(keyB64!);
        var wrapPath = Path.Combine(_root, "config", "move-kit-crypto.json");
        var wrapJson = JsonDocument.Parse(File.ReadAllText(wrapPath));
        var salt = Convert.FromBase64String(wrapJson.RootElement.GetProperty("saltBase64").GetString()!);
        var wrapped = Convert.FromBase64String(wrapJson.RootElement.GetProperty("wrappedKeyBase64").GetString()!);

        var plain = Encoding.UTF8.GetBytes("legacy-v1-payload");
        var kitPath = Path.Combine(_root, "legacy.jotdexkit");
        WriteJdxk1(kitPath, aesKey, salt, wrapped, plain);

        var outZip = Path.Combine(_root, "legacy-out.zip");
        crypto.DecryptToZip(kitPath, password, outZip);
        Assert.Equal(plain, File.ReadAllBytes(outZip));
    }

    private static void WriteJdxk1(string path, byte[] aesKey, byte[] salt, byte[] wrappedKey, byte[] plain)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes(MoveKitCryptoService.MagicV1));
        bw.Write((ushort)salt.Length);
        bw.Write(salt);
        bw.Write((ushort)wrappedKey.Length);
        bw.Write(wrappedKey);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(aesKey, 16))
            aes.Encrypt(nonce, plain, cipher, tag);
        bw.Write(nonce);
        bw.Write(tag);
        bw.Write(cipher);
    }

    private sealed class FixedProbe(bool set) : ILocalAuthProbe
    {
        public bool IsPasswordSet => set;
    }
}
