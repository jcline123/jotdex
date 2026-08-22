using System.Management.Automation.Language;
using Jotdex.Core.CodeDiagnostics;

namespace Jotdex.PowerShellDiagnostics;

/// <summary>
/// Parse-only PowerShell syntax check via <see cref="Parser.ParseInput"/>.
/// Isolated assembly avoids Markdig type conflicts with the main Infrastructure stack.
/// </summary>
public sealed class PowerShellSyntaxParser : IPowerShellSyntaxParser
{
    public IReadOnlyList<CodeDiagnostic> Parse(string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(source))
            return Array.Empty<CodeDiagnostic>();

        if (source.Length > IPowerShellSyntaxParser.MaxInputLength)
            throw new ArgumentException($"Input exceeds maximum length of {IPowerShellSyntaxParser.MaxInputLength} characters.");

        Parser.ParseInput(source, out _, out var parseErrors);

        if (parseErrors is null || parseErrors.Length == 0)
            return Array.Empty<CodeDiagnostic>();

        var list = new List<CodeDiagnostic>(parseErrors.Length);
        foreach (var err in parseErrors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extent = err.Extent;
            list.Add(new CodeDiagnostic(
                Source: "powershell-syntax",
                Severity: CodeDiagnosticSeverity.Error,
                Message: err.Message,
                StartLine: extent.StartLineNumber,
                StartColumn: extent.StartColumnNumber,
                EndLine: extent.EndLineNumber,
                EndColumn: extent.EndColumnNumber,
                Code: err.ErrorId));
        }

        return list;
    }
}
