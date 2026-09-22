using Jotdex.Core.CodeDiagnostics;
using Jotdex.PowerShellDiagnostics;

namespace Jotdex.Unit.Tests.CodeDiagnostics;

public class PowerShellScriptAnalyzerTests
{
    [Fact]
    public void Analyze_reports_alias_warnings_when_module_present()
    {
        EnsureModuleBesideBaseDirectory();

        var analyzer = new PowerShellScriptAnalyzer();
        Assert.True(
            analyzer.IsAvailable,
            $"Expected PSScriptAnalyzer available. LastFailure={PowerShellScriptAnalyzer.LastFailureReason}");

        var findings = analyzer.Analyze("gci | % { $_.Name }");
        Assert.True(
            findings.Count > 0,
            $"Analyzer returned no findings. LastFailure={PowerShellScriptAnalyzer.LastFailureReason}");
        Assert.Contains(findings, d => d.Code == "PSAvoidUsingCmdletAliases");
    }

    static void EnsureModuleBesideBaseDirectory()
    {
        var dest = Path.Combine(AppContext.BaseDirectory, "modules", "PSScriptAnalyzer");
        if (Directory.Exists(dest) &&
            Directory.GetFiles(dest, "PSScriptAnalyzer.psd1", SearchOption.AllDirectories).Length > 0)
        {
            return;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "PowerShellDiagnostics", "modules", "PSScriptAnalyzer");
            if (Directory.Exists(candidate))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                CopyDirectory(candidate, dest);
                return;
            }
            dir = dir.Parent;
        }

        Assert.Fail("Could not locate src/PowerShellDiagnostics/modules/PSScriptAnalyzer for the test.");
    }

    static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var outPath = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.Copy(file, outPath, overwrite: true);
        }
    }
}
