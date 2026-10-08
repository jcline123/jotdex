using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Integrations;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Integrations;

public sealed class IntegrationActivityLog : IIntegrationActivityLog
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<IntegrationActivityLog> _logger;
    private readonly object _gate = new();
    private const int MaxEntries = 500;

    public IntegrationActivityLog(IDataRootResolver dataRoot, ILogger<IntegrationActivityLog> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public void Record(IntegrationActivityEntry entry)
    {
        lock (_gate)
        {
            try
            {
                var dir = Dir();
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "activity.jsonl");
                var line = JsonSerializer.Serialize(entry, JsonOpts);
                File.AppendAllText(path, line + "\n");
                Trim(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write integration activity");
            }
        }
    }

    public IReadOnlyList<IntegrationActivityEntry> Recent(int limit = 50)
    {
        lock (_gate)
        {
            var path = Path.Combine(Dir(), "activity.jsonl");
            if (!File.Exists(path)) return [];
            try
            {
                var lines = File.ReadAllLines(path);
                return lines
                    .Reverse()
                    .Take(Math.Clamp(limit, 1, 200))
                    .Select(l =>
                    {
                        try { return JsonSerializer.Deserialize<IntegrationActivityEntry>(l, JsonOpts); }
                        catch { return null; }
                    })
                    .Where(e => e is not null)
                    .Cast<IntegrationActivityEntry>()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read integration activity");
                return [];
            }
        }
    }

    private void Trim(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length <= MaxEntries) return;
        File.WriteAllLines(path, lines.TakeLast(MaxEntries));
    }

    private string Dir() => Path.Combine(_dataRoot.ResolveDataRoot(), "integrations", "activity");
}
