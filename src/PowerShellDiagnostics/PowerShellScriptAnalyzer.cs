using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Jotdex.Core.CodeDiagnostics;

namespace Jotdex.PowerShellDiagnostics;

/// <summary>
/// Static analysis via bundled PSScriptAnalyzer (Invoke-ScriptAnalyzer -ScriptDefinition).
/// Never executes the user's script. If the module is missing, returns empty results.
/// Import and Invoke always run on the same <see cref="PowerShell"/> instance — module state
/// does not carry across separate <c>PowerShell.Create()</c> calls.
/// </summary>
public sealed class PowerShellScriptAnalyzer : IPowerShellScriptAnalyzer
{
    private static readonly object Gate = new();
    private static string? _manifestPath;
    private static string? _lastFailure;
    private static DateTimeOffset _nextResolveUtc = DateTimeOffset.MinValue;

    public bool IsAvailable
    {
        get
        {
            EnsureManifestResolved();
            lock (Gate) return _manifestPath is not null;
        }
    }

    public static string? LastFailureReason
    {
        get
        {
            lock (Gate) return _lastFailure;
        }
    }

    public IReadOnlyList<CodeDiagnostic> Analyze(string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(source))
            return Array.Empty<CodeDiagnostic>();

        if (source.Length > IPowerShellSyntaxParser.MaxInputLength)
            throw new ArgumentException($"Input exceeds maximum length of {IPowerShellSyntaxParser.MaxInputLength} characters.");

        EnsureManifestResolved();
        string? manifest;
        lock (Gate) manifest = _manifestPath;
        if (manifest is null)
            return Array.Empty<CodeDiagnostic>();

        try
        {
            using var ps = PowerShell.Create();
            ps.AddCommand("Import-Module")
                .AddParameter("Name", manifest)
                .AddParameter("Force");
            ps.Invoke();
            if (ps.HadErrors)
            {
                lock (Gate)
                {
                    _lastFailure = ps.Streams.Error.FirstOrDefault()?.ToString() ?? "Import-Module failed";
                    _manifestPath = null;
                    _nextResolveUtc = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                return Array.Empty<CodeDiagnostic>();
            }

            // Separate invoke — do not AddStatement after Import (Import pipeline output
            // would mix with analyzer results and look like “no findings”).
            ps.Commands.Clear();
            ps.Streams.ClearStreams();
            ps.AddCommand("Invoke-ScriptAnalyzer")
                .AddParameter("ScriptDefinition", source)
                .AddParameter("Severity", new[] { "Warning", "Error" });

            var results = ps.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            if (results.Count == 0 && ps.HadErrors)
            {
                lock (Gate)
                {
                    _lastFailure = ps.Streams.Error.FirstOrDefault()?.ToString() ?? "Invoke-ScriptAnalyzer failed";
                    _manifestPath = null;
                    _nextResolveUtc = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                return Array.Empty<CodeDiagnostic>();
            }

            var list = new List<CodeDiagnostic>(results.Count);
            // DiagnosticRecord.Line/Column are ScriptProperties that need DefaultRunspace.
            var previous = Runspace.DefaultRunspace;
            try
            {
                Runspace.DefaultRunspace = ps.Runspace;
                foreach (var obj in results)
                {
                    var pso = obj as PSObject ?? PSObject.AsPSObject(obj);
                    var severityRaw = pso.Properties["Severity"]?.Value?.ToString() ?? "Warning";
                    var severity = severityRaw.Equals("Error", StringComparison.OrdinalIgnoreCase)
                        ? CodeDiagnosticSeverity.Error
                        : CodeDiagnosticSeverity.Warning;
                    var message = pso.Properties["Message"]?.Value?.ToString() ?? "PSScriptAnalyzer finding";
                    var rule = pso.Properties["RuleName"]?.Value?.ToString();
                    var line = ToPositiveInt(pso.Properties["Line"]?.Value, 1);
                    var col = ToPositiveInt(pso.Properties["Column"]?.Value, 1);

                    list.Add(new CodeDiagnostic(
                        Source: "psscriptanalyzer",
                        Severity: severity,
                        Message: message,
                        StartLine: line,
                        StartColumn: col,
                        EndLine: line,
                        EndColumn: col + 1,
                        Code: rule));
                }
            }
            finally
            {
                Runspace.DefaultRunspace = previous;
            }

            lock (Gate) _lastFailure = null;
            return list;
        }
        catch (Exception ex)
        {
            lock (Gate) _lastFailure = ex.Message;
            return Array.Empty<CodeDiagnostic>();
        }
    }

    private static int ToPositiveInt(object? value, int fallback)
    {
        try
        {
            if (value is null) return fallback;
            var n = Convert.ToInt32(value);
            return n < 1 ? fallback : n;
        }
        catch
        {
            return fallback;
        }
    }

    private static void EnsureManifestResolved()
    {
        lock (Gate)
        {
            if (_manifestPath is not null)
                return;

            if (DateTimeOffset.UtcNow < _nextResolveUtc)
                return;

            try
            {
                var manifest = ResolveModuleManifest();
                if (manifest is null)
                {
                    _lastFailure = "module not found under modules/PSScriptAnalyzer";
                    _nextResolveUtc = DateTimeOffset.UtcNow.AddSeconds(30);
                    return;
                }

                using var ps = PowerShell.Create();
                ps.AddCommand("Import-Module")
                    .AddParameter("Name", manifest)
                    .AddParameter("Force");
                ps.Invoke();
                if (ps.HadErrors)
                {
                    _lastFailure = ps.Streams.Error.FirstOrDefault()?.ToString() ?? "Import-Module failed";
                    _nextResolveUtc = DateTimeOffset.UtcNow.AddSeconds(30);
                    return;
                }

                _manifestPath = manifest;
                _lastFailure = null;
                _nextResolveUtc = DateTimeOffset.MinValue;
            }
            catch (Exception ex)
            {
                _lastFailure = ex.Message;
                _nextResolveUtc = DateTimeOffset.UtcNow.AddSeconds(30);
            }
        }
    }

    private static string? ResolveModuleManifest()
    {
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "modules", "PSScriptAnalyzer"),
            Path.Combine(AppContext.BaseDirectory, "modules"),
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            var manifests = Directory.GetFiles(root, "PSScriptAnalyzer.psd1", SearchOption.AllDirectories);
            if (manifests.Length > 0)
            {
                Array.Sort(manifests, StringComparer.OrdinalIgnoreCase);
                return manifests[^1];
            }
        }

        return null;
    }
}
