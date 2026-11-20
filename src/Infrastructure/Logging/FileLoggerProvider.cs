using System.Text;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Logging;

/// <summary>
/// Appends logs under {dataRoot}/logs/jotdex-yyyyMMdd.log (daily rolling files).
/// Portable / Startup-script installs have no console, so this is the durable trail for CalDAV timeouts and similar.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetainDays = 14;
    private readonly string _logsDir;
    private readonly object _gate = new();
    private DateTime _lastPruneUtc = DateTime.MinValue;

    public FileLoggerProvider(string dataRoot)
    {
        _logsDir = Path.Combine(dataRoot, "logs");
        Directory.CreateDirectory(_logsDir);
    }

    public string LogsDirectory => _logsDir;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose() { }

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {category}: {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        var path = Path.Combine(_logsDir, $"jotdex-{DateTime.Now:yyyyMMdd}.log");
        lock (_gate)
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            PruneOldFilesUnlocked();
        }
    }

    private void PruneOldFilesUnlocked()
    {
        if (DateTime.UtcNow - _lastPruneUtc < TimeSpan.FromHours(6)) return;
        _lastPruneUtc = DateTime.UtcNow;
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-RetainDays);
            foreach (var file in Directory.EnumerateFiles(_logsDir, "jotdex-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length < 15) continue;
                var stamp = name["jotdex-".Length..];
                if (!DateTime.TryParseExact(stamp, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var day))
                    continue;
                if (day.Date < cutoff)
                {
                    try { File.Delete(file); } catch { /* ignore */ }
                }
            }
        }
        catch
        {
            // Logging must never throw into the app.
        }
    }

    public string ReadTail(int maxLines = 200)
    {
        maxLines = Math.Clamp(maxLines, 20, 2000);
        if (!Directory.Exists(_logsDir)) return "(no logs yet)";

        var latest = Directory.EnumerateFiles(_logsDir, "jotdex-*.log")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (latest is null) return "(no log files yet)";

        lock (_gate)
        {
            // Read end of file without loading huge logs fully when possible
            var lines = File.ReadAllLines(latest, Encoding.UTF8);
            if (lines.Length <= maxLines) return string.Join(Environment.NewLine, lines);
            return string.Join(Environment.NewLine, lines.Skip(lines.Length - maxLines));
        }
    }

    public string? LatestLogPath
    {
        get
        {
            if (!Directory.Exists(_logsDir)) return null;
            return Directory.EnumerateFiles(_logsDir, "jotdex-*.log")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
    }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
