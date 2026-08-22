using Jotdex.Core.CodeDiagnostics;

namespace Jotdex.PowerShellDiagnostics;

public sealed class PowerShellDiagnosticsService : IPowerShellDiagnosticsService
{
    private readonly IPowerShellSyntaxParser _syntax;
    private readonly IPowerShellScriptAnalyzer _analyzer;

    public PowerShellDiagnosticsService(IPowerShellSyntaxParser syntax, IPowerShellScriptAnalyzer analyzer)
    {
        _syntax = syntax;
        _analyzer = analyzer;
    }

    public PowerShellDiagnosticsResult Diagnose(string source, CancellationToken cancellationToken = default)
    {
        var syntax = _syntax.Parse(source, cancellationToken);
        var analyzerAvailable = _analyzer.IsAvailable;
        var analyzer = analyzerAvailable ? _analyzer.Analyze(source, cancellationToken) : Array.Empty<CodeDiagnostic>();

        var combined = new List<CodeDiagnostic>(syntax.Count + analyzer.Count);
        combined.AddRange(syntax);
        combined.AddRange(analyzer);

        var status = analyzerAvailable
            ? "PSScriptAnalyzer (static checks; does not run code)"
            : "PSScriptAnalyzer not bundled — syntax check only";

        return new PowerShellDiagnosticsResult(combined, analyzerAvailable, status);
    }
}
