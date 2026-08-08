using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

/// <summary>
/// Move-kit file encryption: AES key lives in DPAPI secrets for unattended daily kits;
/// the same key is password-wrapped (Jotdex unlock password) inside each .jotdexkit for restore on another PC.
/// </summary>
public interface IMoveKitCryptoService
{
    bool IsPasswordProtectionEnabled { get; }
    bool HasEncryptionKey { get; }
    void OnPasswordSet(string password);
    void OnPasswordCleared();
    /// <summary>Initialize or refresh wrap if needed (e.g. upgrade from older install).</summary>
    void EnsureInitialized(string password);
    /// <summary>Encrypt plainZip → .jotdexkit beside it; deletes plain zip on success. Returns encrypted path.</summary>
    string EncryptZipFile(string plainZipPath);
    void DecryptToZip(string encryptedPath, string password, string outputZipPath);
}

public sealed class MoveKitCryptoService : IMoveKitCryptoService
{
    public const string FileExtension = ".jotdexkit";
    private const string Magic = "JDXK1";
    private const string SecretKeyName = "moveKit.aesKey";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Pbkdf2Iterations = 200_000;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ISecretStore _secrets;
    private readonly ILocalAuthProbe _authProbe;
    private readonly ILogger<MoveKitCryptoService> _logger;
    private readonly object _gate = new();

    public MoveKitCryptoService(
        IDataRootResolver dataRoot,
        ISecretStore secrets,
        ILocalAuthProbe authProbe,
        ILogger<MoveKitCryptoService> logger)
    {
        _dataRoot = dataRoot;
        _secrets = secrets;
        _authProbe = authProbe;
        _logger = logger;
    }

    public bool IsPasswordProtectionEnabled => _authProbe.IsPasswordSet;

    public bool HasEncryptionKey => _secrets.Has(SecretKeyName) && File.Exists(WrapPath());

    public void OnPasswordSet(string password)
    {
        if (string.IsNullOrEmpty(password)) return;
        lock (_gate)
        {
            var key = GetOrCreateAesKey();
            PersistWrap(password, key);
            _logger.LogInformation("Move-kit encryption key wrapped with unlock password");
        }
    }

    public void OnPasswordCleared()
    {
        lock (_gate)
        {
            _secrets.Remove(SecretKeyName);
            try { if (File.Exists(WrapPath())) File.Delete(WrapPath()); } catch { /* ignore */ }
            _logger.LogInformation("Move-kit encryption key cleared");
        }
    }

    public void EnsureInitialized(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Password required to initialize move-kit encryption.");
        OnPasswordSet(password);
    }

    public string EncryptZipFile(string plainZipPath)
    {
        if (!File.Exists(plainZipPath))
            throw new FileNotFoundException("Move kit zip not found.", plainZipPath);

        lock (_gate)
        {
            if (!_secrets.TryGet(SecretKeyName, out var keyB64) || string.IsNullOrEmpty(keyB64))
                throw new InvalidOperationException("Move-kit encryption key missing. Unlock password must be set first.");

            var wrap = LoadWrap() ?? throw new InvalidOperationException(
                "Move-kit password wrap missing. Open Settings → Security and re-save your password, or create a kit after unlocking.");

            var aesKey = Convert.FromBase64String(keyB64);
            var plain = File.ReadAllBytes(plainZipPath);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Encrypt(nonce, plain, cipher, tag);

            var outPath = Path.ChangeExtension(plainZipPath, null) + FileExtension;
            using (var fs = File.Create(outPath))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Encoding.ASCII.GetBytes(Magic));
                bw.Write((ushort)wrap.Salt.Length);
                bw.Write(wrap.Salt);
                bw.Write((ushort)wrap.WrappedKey.Length);
                bw.Write(wrap.WrappedKey);
                bw.Write(nonce);
                bw.Write(tag);
                bw.Write(cipher);
            }

            try { File.Delete(plainZipPath); } catch { /* keep enc even if delete fails */ }
            _logger.LogInformation("Encrypted move kit → {Path}", outPath);
            return outPath;
        }
    }

    public void DecryptToZip(string encryptedPath, string password, string outputZipPath)
    {
        var bytes = File.ReadAllBytes(encryptedPath);
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);
        var magic = Encoding.ASCII.GetString(br.ReadBytes(Magic.Length));
        if (magic != Magic)
            throw new InvalidDataException("Not a Jotdex encrypted move kit.");

        var saltLen = br.ReadUInt16();
        var salt = br.ReadBytes(saltLen);
        var wrapLen = br.ReadUInt16();
        var wrapped = br.ReadBytes(wrapLen);
        var nonce = br.ReadBytes(NonceSize);
        var tag = br.ReadBytes(TagSize);
        var cipher = br.ReadBytes((int)(ms.Length - ms.Position));

        var kek = DeriveKek(password, salt);
        var aesKey = UnwrapKey(kek, wrapped);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(aesKey, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);

        Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
        File.WriteAllBytes(outputZipPath, plain);
    }

    private byte[] GetOrCreateAesKey()
    {
        if (_secrets.TryGet(SecretKeyName, out var existing) && !string.IsNullOrEmpty(existing))
            return Convert.FromBase64String(existing);
        var key = RandomNumberGenerator.GetBytes(KeySize);
        _secrets.Set(SecretKeyName, Convert.ToBase64String(key));
        return key;
    }

    private void PersistWrap(string password, byte[] aesKey)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var kek = DeriveKek(password, salt);
        var wrapped = WrapKey(kek, aesKey);
        var file = new WrapFile
        {
            SaltBase64 = Convert.ToBase64String(salt),
            WrappedKeyBase64 = Convert.ToBase64String(wrapped),
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        var path = WrapPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private WrapMaterial? LoadWrap()
    {
        var path = WrapPath();
        if (!File.Exists(path)) return null;
        try
        {
            var file = JsonSerializer.Deserialize<WrapFile>(File.ReadAllText(path), JsonOpts);
            if (file is null || string.IsNullOrEmpty(file.SaltBase64) || string.IsNullOrEmpty(file.WrappedKeyBase64))
                return null;
            return new WrapMaterial
            {
                Salt = Convert.FromBase64String(file.SaltBase64),
                WrappedKey = Convert.FromBase64String(file.WrappedKeyBase64)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read move-kit wrap file");
            return null;
        }
    }

    private string WrapPath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "config", "move-kit-crypto.json");

    private static byte[] DeriveKek(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    private static byte[] WrapKey(byte[] kek, byte[] aesKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[aesKey.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(kek, TagSize))
            aes.Encrypt(nonce, aesKey, cipher, tag);
        var result = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, result, NonceSize + TagSize, cipher.Length);
        return result;
    }

    private static byte[] UnwrapKey(byte[] kek, byte[] wrapped)
    {
        if (wrapped.Length < NonceSize + TagSize + KeySize)
            throw new InvalidDataException("Invalid wrapped key.");
        var nonce = wrapped.AsSpan(0, NonceSize);
        var tag = wrapped.AsSpan(NonceSize, TagSize);
        var cipher = wrapped.AsSpan(NonceSize + TagSize);
        var aesKey = new byte[cipher.Length];
        using (var aes = new AesGcm(kek, TagSize))
            aes.Decrypt(nonce, cipher, tag, aesKey);
        return aesKey;
    }

    private sealed class WrapFile
    {
        public string SaltBase64 { get; set; } = "";
        public string WrappedKeyBase64 { get; set; } = "";
        public DateTimeOffset? UpdatedUtc { get; set; }
    }

    private sealed class WrapMaterial
    {
        public required byte[] Salt { get; init; }
        public required byte[] WrappedKey { get; init; }
    }
}

/// <summary>Avoid circular DI: auth reports whether a password exists.</summary>
public interface ILocalAuthProbe
{
    bool IsPasswordSet { get; }
}
