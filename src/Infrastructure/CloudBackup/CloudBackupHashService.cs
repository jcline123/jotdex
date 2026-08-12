using System.Security.Cryptography;
using System.Text;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupHashService
{
    public const int DropboxBlockSize = 4 * 1024 * 1024;

    public string Sha256FileHex(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256StreamHex(stream);
    }

    public string Sha256StreamHex(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Dropbox content hash: SHA256 of each 4 MiB block, concatenate digests, SHA256 that result.
    /// See https://www.dropbox.com/developers/reference/content-hash
    /// </summary>
    public string DropboxContentHashHex(string path)
    {
        using var stream = File.OpenRead(path);
        return DropboxContentHashHex(stream);
    }

    public string DropboxContentHashHex(Stream stream)
    {
        using var overall = SHA256.Create();
        var buffer = new byte[DropboxBlockSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var blockHash = SHA256.HashData(buffer.AsSpan(0, read));
            overall.TransformBlock(blockHash, 0, blockHash.Length, null, 0);
        }

        overall.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(overall.Hash!).ToLowerInvariant();
    }

    public string Md5FileHex(string path)
    {
        using var stream = File.OpenRead(path);
#pragma warning disable CA5351 // MD5 required for Google Drive checksum comparison
        var hash = MD5.HashData(stream);
#pragma warning restore CA5351
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Utf8Sha256Hex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
