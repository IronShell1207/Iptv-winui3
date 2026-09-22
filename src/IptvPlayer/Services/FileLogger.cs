using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace IptvPlayer.Services;

/// <summary>
/// Простой файловый лог с ротацией: пишет в фоновом потоке, чтобы не задерживать UI.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int KeepFiles = 5;

    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly string _folder;
    private readonly Thread _worker;
    private readonly LogLevel _minLevel;
    private bool _disposed;

    public FileLoggerProvider(string folder, LogLevel minLevel)
    {
        _folder = folder;
        _minLevel = minLevel;
        Directory.CreateDirectory(folder);

        _worker = new Thread(Drain) { IsBackground = true, Name = "file-log" };
        _worker.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName, _minLevel);

    internal void Enqueue(string line)
    {
        if (_disposed) return;
        _queue.TryAdd(line);
    }

    private void Drain()
    {
        var path = Path.Combine(_folder, "app.log");
        var buffer = new StringBuilder();

        foreach (var line in _queue.GetConsumingEnumerable())
        {
            buffer.Clear().AppendLine(line);

            while (_queue.TryTake(out var more))
                buffer.AppendLine(more);

            try
            {
                Rotate(path);
                File.AppendAllText(path, buffer.ToString(), Encoding.UTF8);
            }
            catch (IOException)
            {
                // лог не должен мешать работе приложения
            }
        }
    }

    private static void Rotate(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes) return;

        var dir = Path.GetDirectoryName(path)!;
        var oldest = Path.Combine(dir, $"app.{KeepFiles}.log");
        if (File.Exists(oldest)) File.Delete(oldest);

        for (var i = KeepFiles - 1; i >= 1; i--)
        {
            var from = Path.Combine(dir, $"app.{i}.log");
            if (File.Exists(from)) File.Move(from, Path.Combine(dir, $"app.{i + 1}.log"), overwrite: true);
        }

        File.Move(path, Path.Combine(dir, "app.1.log"), overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category, LogLevel minLevel) : ILogger
    {
        private readonly string _shortCategory = category.Split('.')[^1];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{Short(logLevel)}] {_shortCategory}: {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;

            provider.Enqueue(line);
        }

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
