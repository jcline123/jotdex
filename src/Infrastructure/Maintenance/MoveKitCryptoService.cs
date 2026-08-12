using System.Buffers.Binary;
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
/// New kits use streaming JDXK2; JDXK1 remains decryptable.
/// </summary>
public interface IMoveKitCryptoService
{
    bool IsPasswordProtectionEnabled { get; }
    bool HasEncryptionKey { get; }
    void OnPasswordSet(string password);
    void OnPasswordCleared();
    void EnsureInitialized(string password);
    /// <summary>Encrypt plainZip → .jotdexkit (JDXK2); deletes plain zip on success. Returns encrypted path.</summary>
    string EncryptZipFile(string plainZipPath);
    void DecryptToZip(string encryptedPath, string password, string outputZipPath);
}

public sealed class MoveKitCryptoService : IMoveKitCryptoService
{
    public const string FileExtension = ".jotdexkit";
    public const string MagicV1 = "JDXK1";
    public const string MagicV2 = "JDXK2";
    public const int ChunkSize = 4 * 1024 * 1024;

    private const string SecretKeyName = "moveKit.aesKey";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int NoncePrefixSize = 8;
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
            var outPath = Path.ChangeExtension(plainZipPath, null) + FileExtension;
            var tmpPath = outPath + ".partial";
            try
            {
                EncryptV2Streaming(plainZipPath, tmpPath, aesKey, wrap);
                File.Move(tmpPath, outPath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* ignore */ }
                throw;
            }

            try { File.Delete(plainZipPath); } catch { /* keep enc even if delete fails */ }
            _logger.LogInformation("Encrypted move kit (JDXK2) → {Path}", outPath);
            return outPath;
        }
    }

    public void DecryptToZip(string encryptedPath, string password, string outputZipPath)
    {
        using var fs = File.OpenRead(encryptedPath);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
        var magic = Encoding.ASCII.GetString(br.ReadBytes(5));
        Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
        var tmp = outputZipPath + ".partial";
        try
        {
            if (magic == MagicV2)
                DecryptV2Streaming(br, password, tmp);
            else if (magic == MagicV1)
                DecryptV1Buffered(br, password, tmp);
            else
                throw new InvalidDataException("Not a Jotdex encrypted move kit.");
            File.Move(tmp, outputZipPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
            throw;
        }
    }

    private static void EncryptV2Streaming(string plainZipPath, string outPath, byte[] aesKey, WrapMaterial wrap)
    {
        var plainLen = new FileInfo(plainZipPath).Length;
        if (plainLen < 0) throw new InvalidDataException("Invalid zip length.");
        var chunkCount = (uint)((plainLen + ChunkSize - 1) / ChunkSize);
        if (plainLen == 0) chunkCount = 1;
        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);

        using var input = File.OpenRead(plainZipPath);
        using var output = File.Create(outPath);
        using var bw = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        bw.Write(Encoding.ASCII.GetBytes(MagicV2));
        bw.Write((ushort)wrap.Salt.Length);
        bw.Write(wrap.Salt);
        bw.Write((ushort)wrap.WrappedKey.Length);
        bw.Write(wrap.WrappedKey);
        bw.Write(plainLen);
        bw.Write(chunkCount);
        bw.Write(noncePrefix);

        var plainBuf = new byte[ChunkSize];
        var cipherBuf = new byte[ChunkSize];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(aesKey, TagSize);

        for (uint i = 0; i < chunkCount; i++)
        {
            var read = input.Read(plainBuf, 0, ChunkSize);
            if (i < chunkCount - 1 && read != ChunkSize)
                throw new InvalidDataException("Unexpected end of zip while encrypting.");
            if (i == chunkCount - 1 && plainLen == 0 && read != 0)
                throw new InvalidDataException("Unexpected bytes for empty zip.");
            if (i == chunkCount - 1 && plainLen > 0 && read != (int)(plainLen - (long)i * ChunkSize))
                throw new InvalidDataException("Final chunk length mismatch.");

            var nonce = BuildChunkNonce(noncePrefix, i);
            var aad = BuildChunkAad(i, (uint)read);
            var cipherSpan = cipherBuf.AsSpan(0, read);
            aes.Encrypt(nonce, plainBuf.AsSpan(0, read), cipherSpan, tag, aad);
            bw.Write((uint)read);
            bw.Write(cipherSpan);
            bw.Write(tag);
        }
    }

    private static void DecryptV2Streaming(BinaryReader br, string password, string outputZipPath)
    {
        var saltLen = br.ReadUInt16();
        var salt = br.ReadBytes(saltLen);
        var wrapLen = br.ReadUInt16();
        var wrapped = br.ReadBytes(wrapLen);
        var plainLen = br.ReadInt64();
        var chunkCount = br.ReadUInt32();
        var noncePrefix = br.ReadBytes(NoncePrefixSize);
        if (noncePrefix.Length != NoncePrefixSize)
            throw new InvalidDataException("Invalid JDXK2 nonce prefix.");

        var kek = DeriveKek(password, salt);
        var aesKey = UnwrapKey(kek, wrapped);
        using var output = File.Create(outputZipPath);
        using var aes = new AesGcm(aesKey, TagSize);
        long written = 0;
        var cipherBuf = new byte[ChunkSize];
        var plainBuf = new byte[ChunkSize];
        var tag = new byte[TagSize];

        for (uint i = 0; i < chunkCount; i++)
        {
            var chunkPlainLen = br.ReadUInt32();
            if (chunkPlainLen > ChunkSize)
                throw new InvalidDataException("JDXK2 chunk too large.");
            var cipher = br.ReadBytes((int)chunkPlainLen);
            if (cipher.Length != chunkPlainLen)
                throw new InvalidDataException("Truncated JDXK2 chunk.");
            if (br.Read(tag, 0, TagSize) != TagSize)
                throw new InvalidDataException("Missing JDXK2 auth tag.");

            var nonce = BuildChunkNonce(noncePrefix, i);
            var aad = BuildChunkAad(i, chunkPlainLen);
            var plainSpan = plainBuf.AsSpan(0, (int)chunkPlainLen);
            aes.Decrypt(nonce, cipher, tag, plainSpan, aad);
            output.Write(plainSpan);
            written += chunkPlainLen;
        }

        if (written != plainLen)
            throw new InvalidDataException("JDXK2 plaintext length mismatch.");
        if (br.BaseStream.Position != br.BaseStream.Length)
            throw new InvalidDataException("Extra data after JDXK2 payload.");
    }

    private static void DecryptV1Buffered(BinaryReader br, string password, string outputZipPath)
    {
        var saltLen = br.ReadUInt16();
        var salt = br.ReadBytes(saltLen);
        var wrapLen = br.ReadUInt16();
        var wrapped = br.ReadBytes(wrapLen);
        var nonce = br.ReadBytes(NonceSize);
        var tag = br.ReadBytes(TagSize);
        var cipher = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position));

        var kek = DeriveKek(password, salt);
        var aesKey = UnwrapKey(kek, wrapped);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(aesKey, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain);
        File.WriteAllBytes(outputZipPath, plain);
    }

    private static byte[] BuildChunkNonce(byte[] prefix, uint index)
    {
        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(prefix, 0, nonce, 0, NoncePrefixSize);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixSize), index);
        return nonce;
    }

    private static byte[] BuildChunkAad(uint index, uint plainLen)
    {
        var aad = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(0), index);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(4), plainLen);
        return aad;
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

    private static byte[] DeriveKek(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

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

public interface ILocalAuthProbe
{
    bool IsPasswordSet { get; }
}
