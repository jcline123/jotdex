namespace Jotdex.Core.CodeDiagnostics;

public enum CodeDiagnosticSeverity
{
    Error,
    Warning,
    Style
}

public sealed record CodeDiagnostic(
    string Source,
    CodeDiagnosticSeverity Severity,
    string Message,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string? Code = null);

public interface IPowerShellSyntaxParser
{
    const int MaxInputLength = 262_144;

    IReadOnlyList<CodeDiagnostic> Parse(string source, CancellationToken cancellationToken = default);
}
