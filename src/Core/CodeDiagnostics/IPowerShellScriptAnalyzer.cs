namespace Jotdex.Core.CodeDiagnostics;

public interface IPowerShellScriptAnalyzer
{
    bool IsAvailable { get; }
    IReadOnlyList<CodeDiagnostic> Analyze(string source, CancellationToken cancellationToken = default);
}

public interface IPowerShellDiagnosticsService
{
    PowerShellDiagnosticsResult Diagnose(string source, CancellationToken cancellationToken = default);
}

public sealed record PowerShellDiagnosticsResult(
    IReadOnlyList<CodeDiagnostic> Diagnostics,
    bool ScriptAnalyzerAvailable,
    string? ScriptAnalyzerStatus);
