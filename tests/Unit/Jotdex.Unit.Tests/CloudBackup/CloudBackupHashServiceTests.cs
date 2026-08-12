using System.Text;
using Jotdex.Infrastructure.CloudBackup;

namespace Jotdex.Unit.Tests.CloudBackup;

public class CloudBackupHashServiceTests : IDisposable
{
    private readonly string _root;

    public CloudBackupHashServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void DropboxContentHash_empty_file_matches_sha256_of_empty()
    {
        var path = Path.Combine(_root, "empty.bin");
        File.WriteAllBytes(path, Array.Empty<byte>());
        var hash = new CloudBackupHashService().DropboxContentHashHex(path);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public void DropboxContentHash_Hello_World_newline_known_vector()
    {
        // Known vector from rclone / community examples: "Hello, World!\n"
        var path = Path.Combine(_root, "hello.txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("Hello, World!\n"));
        var hash = new CloudBackupHashService().DropboxContentHashHex(path);
        Assert.Equal("aa4aeabf82d0f32ed81807b2ddbb48e6d3bf58c7598a835651895e5ecb282e77", hash);
    }

    [Fact]
    public void DropboxContentHash_stream_matches_file()
    {
        var path = Path.Combine(_root, "blob.bin");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("jotdex-cloud-backup"));
        var svc = new CloudBackupHashService();
        var fromFile = svc.DropboxContentHashHex(path);
        using var stream = File.OpenRead(path);
        Assert.Equal(fromFile, svc.DropboxContentHashHex(stream));
    }
}
