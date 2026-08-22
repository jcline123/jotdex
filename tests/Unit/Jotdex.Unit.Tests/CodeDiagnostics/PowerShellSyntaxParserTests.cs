using Jotdex.Core.CodeDiagnostics;
using Jotdex.PowerShellDiagnostics;

namespace Jotdex.Unit.Tests.CodeDiagnostics;

public class PowerShellSyntaxParserTests
{
    private readonly PowerShellSyntaxParser _parser = new();

    [Fact]
    public void Valid_script_returns_no_errors()
    {
        var code = "Get-Service -Name Spooler\nRestart-Service -Name Spooler";
        var diagnostics = _parser.Parse(code);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Missing_brace_reports_parse_error()
    {
        var code = "if ($true) { Write-Output 'hi'";
        var diagnostics = _parser.Parse(code);
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d => Assert.Equal(CodeDiagnosticSeverity.Error, d.Severity));
        Assert.Contains(diagnostics, d => d.Message.Contains("Missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unterminated_string_reports_parse_error()
    {
        var code = "$x = 'unterminated";
        var diagnostics = _parser.Parse(code);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Empty(_parser.Parse(""));
        Assert.Empty(_parser.Parse("   "));
    }

    [Fact]
    public void Oversized_input_throws()
    {
        var huge = new string('a', IPowerShellSyntaxParser.MaxInputLength + 1);
        Assert.Throws<ArgumentException>(() => _parser.Parse(huge));
    }
}
