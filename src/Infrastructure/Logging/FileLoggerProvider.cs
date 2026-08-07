using System.Text;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Logging;

/// <summary>Appends logs under {dataRoot}/logs/jotdex-yyyyMMdd.log for offline troubleshooting.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logsDir;
    private readonly object _gate = new();

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
