using Jotdex.Core.CodeDiagnostics;

namespace Jotdex.Server.CodeDiagnostics;

public static class CodeDiagnosticsEndpointExtensions
{
    private const int ParseTimeoutSeconds = 8;

    public static IEndpointRouteBuilder MapCodeDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/code-diagnostics/powershell", async (HttpRequest request, IPowerShellDiagnosticsService diagnostics, CancellationToken ct) =>
        {
            PowerShellDiagnosticsBody? body;
            try
            {
                body = await request.ReadFromJsonAsync<PowerShellDiagnosticsBody>(ct);
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON body." });
            }

            if (body?.Code is null)
                return Results.BadRequest(new { error = "Missing code." });

            if (body.Code.Length > IPowerShellSyntaxParser.MaxInputLength)
                return Results.BadRequest(new { error = $"Code exceeds maximum length of {IPowerShellSyntaxParser.MaxInputLength} characters." });

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(ParseTimeoutSeconds));
                var result = diagnostics.Diagnose(body.Code, timeout.Token);
                return Results.Json(new
                {
                    label = "PowerShell syntax",
                    scriptAnalyzerAvailable = result.ScriptAnalyzerAvailable,
                    scriptAnalyzerStatus = result.ScriptAnalyzerStatus,
                    diagnostics = result.Diagnostics.Select(DiagnosticDto.From).ToArray()
                });
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Json(new { error = "Diagnostics timed out." }, statusCode: StatusCodes.Status408RequestTimeout);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }

    private sealed class PowerShellDiagnosticsBody
    {
        public string? Code { get; set; }
    }

    private sealed record DiagnosticDto(
        string Source,
        string Severity,
        string Message,
        int StartLine,
        int StartColumn,
        int EndLine,
        int EndColumn,
        string? Code)
    {
        public static DiagnosticDto From(CodeDiagnostic d) => new(
            d.Source,
            d.Severity.ToString().ToLowerInvariant(),
            d.Message,
            d.StartLine,
            d.StartColumn,
            d.EndLine,
            d.EndColumn,
            d.Code);
    }
}
