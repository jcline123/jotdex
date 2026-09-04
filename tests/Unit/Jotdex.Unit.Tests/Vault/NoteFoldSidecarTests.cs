using Jotdex.Infrastructure.Vault;

namespace Jotdex.Unit.Tests.Vault;

public sealed class NoteFoldSidecarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jotdex-folds-" + Guid.NewGuid().ToString("N"));

    public NoteFoldSidecarTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* temp */ }
    }

    [Fact]
    public void Path_is_sibling_folds_json()
    {
        var md = Path.Combine(_dir, "OPNsense IPsec VPN.md");
        var folds = NoteFoldSidecar.PathBesideMarkdown(md);
        Assert.Equal(Path.Combine(_dir, "OPNsense IPsec VPN.folds.json"), folds);
    }

    [Fact]
    public void Round_trips_collapsed_keys_and_deletes_when_empty()
    {
        var md = Path.Combine(_dir, "Note.md");
        File.WriteAllText(md, "# Note\n");
        var path = NoteFoldSidecar.PathBesideMarkdown(md);
        File.WriteAllText(path, NoteFoldSidecar.Serialize(["1:1:Setup", "2:1:Later", "not-a-key", "1:1:Setup"]));
        var read = NoteFoldSidecar.ReadCollapsed(md);
        Assert.Equal(new[] { "1:1:Setup", "2:1:Later" }, read);

        NoteFoldSidecar.DeleteBeside(md);
        Assert.False(File.Exists(path));
        Assert.Empty(NoteFoldSidecar.ReadCollapsed(md));
    }

    [Fact]
    public void Move_and_copy_keep_the_helper_with_the_note()
    {
        var md = Path.Combine(_dir, "Orig.md");
        File.WriteAllText(md, "# Orig\n");
        File.WriteAllText(NoteFoldSidecar.PathBesideMarkdown(md), NoteFoldSidecar.Serialize(["1:1:Orig"]));

        var copy = Path.Combine(_dir, "Orig copy.md");
        File.WriteAllText(copy, "# Copy\n");
        NoteFoldSidecar.CopyBeside(md, copy);
        Assert.Equal(new[] { "1:1:Orig" }, NoteFoldSidecar.ReadCollapsed(copy));

        var moved = Path.Combine(_dir, "Renamed.md");
        File.Move(md, moved);
        NoteFoldSidecar.MoveBeside(md, moved);
        Assert.False(File.Exists(NoteFoldSidecar.PathBesideMarkdown(md)));
        Assert.Equal(new[] { "1:1:Orig" }, NoteFoldSidecar.ReadCollapsed(moved));
    }
}
