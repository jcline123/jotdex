using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Jotdex.Core.CodeDiagnostics;

namespace Jotdex.PowerShellDiagnostics;

/// <summary>
/// Static analysis via bundled PSScriptAnalyzer (Invoke-ScriptAnalyzer -ScriptDefinition).
/// Never executes the user's script. If the module is missing, returns empty results.
/// </summary>
public sealed class PowerShellScriptAnalyzer : IPowerShellScriptAnalyzer
{
    private static readonly object InitGate = new();
    private static bool _initAttempted;
    private static bool _available;

    public bool IsAvailable
    {
        get
        {
            EnsureModule();
            return _available;
        }
    }

    public IReadOnlyList<CodeDiagnostic> Analyze(string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(source))
            return Array.Empty<CodeDiagnostic>();

        if (source.Length > IPowerShellSyntaxParser.MaxInputLength)
            throw new ArgumentException($"Input exceeds maximum length of {IPowerShellSyntaxParser.MaxInputLength} characters.");

        EnsureModule();
        if (!_available)
            return Array.Empty<CodeDiagnostic>();

        try
        {
            using var ps = PowerShell.Create();
            ps.AddCommand("Invoke-ScriptAnalyzer")
                .AddParameter("ScriptDefinition", source)
                .AddParameter("Severity", new[] { "Warning", "Error" });

            var results = ps.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            if (ps.HadErrors)
                return Array.Empty<CodeDiagnostic>();

            var list = new List<CodeDiagnostic>();
            foreach (var obj in results)
            {
                if (obj is not PSObject pso) continue;
                var severityRaw = pso.Properties["Severity"]?.Value?.ToString() ?? "Warning";
                var severity = severityRaw.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    ? CodeDiagnosticSeverity.Error
                    : CodeDiagnosticSeverity.Warning;
                var message = pso.Properties["Message"]?.Value?.ToString() ?? "PSScriptAnalyzer finding";
                var rule = pso.Properties["RuleName"]?.Value?.ToString();
                var line = Convert.ToInt32(pso.Properties["Line"]?.Value ?? 1);
                var col = Convert.ToInt32(pso.Properties["Column"]?.Value ?? 1);
                var endLine = line;
                var endCol = col + 1;

                list.Add(new CodeDiagnostic(
                    Source: "psscriptanalyzer",
                    Severity: severity,
                    Message: message,
                    StartLine: Math.Max(1, line),
                    StartColumn: Math.Max(1, col),
                    EndLine: Math.Max(1, endLine),
                    EndColumn: Math.Max(1, endCol),
                    Code: rule));
            }

            return list;
        }
        catch (Exception)
        {
            return Array.Empty<CodeDiagnostic>();
        }
    }

    private static void EnsureModule()
    {
        if (_initAttempted) return;
        lock (InitGate)
        {
            if (_initAttempted) return;
            _initAttempted = true;
            try
            {
                var moduleRoot = Path.Combine(AppContext.BaseDirectory, "modules", "PSScriptAnalyzer");
                if (!Directory.Exists(moduleRoot))
                {
                    _available = false;
                    return;
                }

                using var ps = PowerShell.Create();
                ps.AddCommand("Import-Module")
                    .AddParameter("Name", moduleRoot)
                    .AddParameter("Force");
                ps.Invoke();
                _available = !ps.HadErrors;
            }
            catch
            {
                _available = false;
            }
        }
    }
}
