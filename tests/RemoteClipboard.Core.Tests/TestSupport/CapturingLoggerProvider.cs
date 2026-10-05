using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RemoteClipboard.Core.Tests.TestSupport;

/// <summary>Collects every formatted log line (including exceptions) to assert privacy rules.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Lines);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category} {formatter(state, exception)} {exception}");
    }
}
