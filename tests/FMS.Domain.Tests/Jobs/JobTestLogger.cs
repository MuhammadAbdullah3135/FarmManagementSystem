using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FMS.Domain.Tests.Jobs;

/// <summary>A single log entry captured from a job under test.</summary>
public sealed record CapturedLog(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>
/// Captures log entries so tests can prove a job failure is *visible* (logged
/// with the structured job/farm detail) rather than silently swallowed.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyCollection<CapturedLog> Entries => _entries.ToArray();

    public IEnumerable<CapturedLog> ForLevel(LogLevel level) => Entries.Where(e => e.Level == level);

    public bool HasMessageContaining(string fragment) =>
        Entries.Any(e => e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentQueue<CapturedLog> _entries;

        public CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries)
        {
            _category = category;
            _entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(new CapturedLog(logLevel, _category, formatter(state, exception), exception));
        }
    }
}

public static class TestLoggers
{
    /// <summary>Creates a logger for <typeparamref name="T"/> plus the provider capturing its output.</summary>
    public static (ILogger<T> Logger, CapturingLoggerProvider Provider) Create<T>()
    {
        var provider = new CapturingLoggerProvider();
        var logger = provider.CreateLogger(typeof(T).FullName ?? typeof(T).Name);
        return (new LoggerAdapter<T>(logger), provider);
    }

    private sealed class LoggerAdapter<T> : ILogger<T>
    {
        private readonly ILogger _inner;

        public LoggerAdapter(ILogger inner)
        {
            _inner = inner;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
