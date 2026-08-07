using Jotdex.Core.Vault;
using Markdig;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Vault;

public sealed class MarkdigMarkdownRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public string ToHtml(string markdown) => Markdown.ToHtml(markdown ?? "", _pipeline);
}

public sealed class VaultFileWatcher : IHostedService, IDisposable
{
    private readonly IVaultService _vault;
    private readonly IVaultPathGuard _paths;
    private readonly ILogger<VaultFileWatcher> _logger;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(750);
    private Timer? _periodic;

    public VaultFileWatcher(IVaultService vault, IVaultPathGuard paths, ILogger<VaultFileWatcher> logger)
    {
        _vault = vault;
        _paths = paths;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_paths.IsConfigured) return Task.CompletedTask;

        try
        {
            _watcher = new FileSystemWatcher(_paths.VaultRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += OnEvent;
            _watcher.Created += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += (_, e) =>
            {
                _logger.LogWarning(e.GetException(), "Vault watcher error; scheduling full rescan");
                ScheduleRescan();
            };
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start vault file watcher");
        }

        _periodic = new Timer(_ =>
        {
            try { _vault.Rescan(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Periodic vault rescan failed"); }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _periodic?.Dispose();
        _debounceCts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _periodic?.Dispose();
        _debounceCts?.Dispose();
    }

    private void OnRenamed(object sender, RenamedEventArgs e) => ScheduleRescanIfNeeded(e.FullPath, e.OldFullPath);
    private void OnEvent(object sender, FileSystemEventArgs e) => ScheduleRescanIfNeeded(e.FullPath);

    private void ScheduleRescanIfNeeded(params string[] paths)
    {
        // Skip events that are only atomic-write temps / editor junk (false conflict source)
        if (paths.Length > 0 && paths.All(IsJunkPath))
            return;
        ScheduleRescan();
    }

    private static bool IsJunkPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        var name = Path.GetFileName(path);
        return name.StartsWith(".", StringComparison.Ordinal) ||
               name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("~", StringComparison.Ordinal);
    }

    private void ScheduleRescan()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounce, token);
                _vault.Rescan();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Debounced vault rescan failed");
            }
        }, token);
    }
}
