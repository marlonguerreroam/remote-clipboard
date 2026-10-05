using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace RemoteClipboard.Core.Logging;

/// <summary>
/// Minimal daily-rotating file logger (no third-party dependency). Writes happen on a background task.
/// Only technical events reach it: <see cref="Log"/> never accepts clipboard content.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly int _retainedFiles;
    private readonly TimeProvider _time;
    private readonly Channel<string> _lines = Channel.CreateBounded<string>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly Task _writer;

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information, int retainedFiles = 7, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _minimumLevel = minimumLevel;
        _retainedFiles = retainedFiles;
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(directory);
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, ShortName(name)));

    public void Dispose()
    {
        _lines.Writer.TryComplete();
        _writer.Wait(TimeSpan.FromSeconds(2));
    }

    private void Enqueue(string line) => _lines.Writer.TryWrite(line);

    private async Task WriteLoopAsync()
    {
        string? currentPath = null;
        StreamWriter? writer = null;
        try
        {
            await foreach (var line in _lines.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var path = Path.Combine(_directory, $"remoteclipboard-{_time.GetLocalNow():yyyyMMdd}.log");
                if (path != currentPath)
                {
                    if (writer is not null)
                    {
                        await writer.DisposeAsync().ConfigureAwait(false);
                    }

                    writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                    currentPath = path;
                    DeleteOldFiles();
                }

                await writer!.WriteLineAsync(line).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Logging must never take the application down.
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void DeleteOldFiles()
    {
        foreach (var old in Directory.GetFiles(_directory, "remoteclipboard-*.log").OrderDescending(StringComparer.Ordinal).Skip(_retainedFiles))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string ShortName(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = string.Create(CultureInfo.InvariantCulture,
                $"{provider._time.GetLocalNow():yyyy-MM-dd HH:mm:ss.fff zzz} [{Abbreviation(logLevel)}] {category}: {formatter(state, exception)}");
            if (exception is not null)
            {
                // Type and message only; stack traces are useful but message text never carries clipboard data.
                line += $" | {exception.GetType().Name}: {exception.Message}";
            }

            provider.Enqueue(line);
        }

        private static string Abbreviation(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "CRT",
        };
    }
}
