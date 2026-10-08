using Jotdex.Core.Integrations;

namespace Jotdex.Infrastructure.Integrations;

public sealed class IntegrationFolderAccess : IIntegrationFolderAccess
{
    public bool AllowsFolder(IntegrationTokenRecord token, string? folderRelativePath)
    {
        if (token.WholeVault) return true;
        var folder = Normalize(folderRelativePath ?? "");
        if (folder.Length == 0)
        {
            // Root listing: only if a root "" was granted (unusual) — whole vault covers root.
            return token.AllowedFolderRoots.Any(r => Normalize(r).Length == 0);
        }
        foreach (var root in token.AllowedFolderRoots)
        {
            var n = Normalize(root);
            if (n.Length == 0) continue;
            if (folder.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
            if (folder.StartsWith(n + "/", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public bool AllowsNotePath(IntegrationTokenRecord token, string noteRelativePath)
    {
        if (token.WholeVault) return true;
        var path = Normalize(noteRelativePath);
        if (path.Length == 0) return false;
        // Reject traversal / absolute / UNC style after normalize
        if (path.Contains("..", StringComparison.Ordinal) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains(':', StringComparison.Ordinal))
            return false;

        var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
        return AllowsFolder(token, dir);
    }

    public IReadOnlyList<string> FilterFolders(IntegrationTokenRecord token, IEnumerable<string> folders)
    {
        if (token.WholeVault) return folders.Select(Normalize).Where(f => f.Length > 0 || true).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return folders.Select(Normalize).Where(f => AllowsFolder(token, f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Normalize(string path)
    {
        var p = (path ?? "").Replace('\\', '/').Trim();
        while (p.Contains("//", StringComparison.Ordinal))
            p = p.Replace("//", "/", StringComparison.Ordinal);
        return p.Trim('/');
    }
}
