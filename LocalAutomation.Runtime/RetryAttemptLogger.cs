using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace LocalAutomation.Runtime;

/// <summary>
/// Forwards one retry attempt's logs immediately while keeping a copy for retry classification.
/// </summary>
internal sealed class RetryAttemptLogger : ILogger
{
    private readonly ILogger _innerLogger;
    private readonly object _syncRoot = new();
    private readonly List<ExecutionRetryLogEntry> _entries = new();

    /// <summary>
    /// Creates an attempt logger around the task logger that owns the persisted session log stream.
    /// </summary>
    public RetryAttemptLogger(ILogger innerLogger)
    {
        _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
    }

    /// <summary>
    /// Gets a stable snapshot of every formatted log entry captured during the attempt.
    /// </summary>
    public IReadOnlyList<ExecutionRetryLogEntry> Entries
    {
        get
        {
            lock (_syncRoot)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>
    /// Writes the entry normally and captures the formatted message and exception payload for retry classification.
    /// </summary>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (formatter == null)
        {
            return;
        }

        _innerLogger.Log(logLevel, eventId, state, exception, formatter);

        string message = formatter(state, exception);
        if (exception != null)
        {
            message += Environment.NewLine + exception;
        }

        lock (_syncRoot)
        {
            _entries.Add(new ExecutionRetryLogEntry(logLevel, message));
        }
    }

    /// <summary>
    /// Returns true for every log level so attempt classification sees the same messages the task logger receives.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <summary>
    /// Opens a matching scope on the wrapped task logger.
    /// </summary>
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return _innerLogger.BeginScope(state) ?? NullScope.Instance;
    }

    /// <summary>
    /// Provides the no-op scope object used when the wrapped logger does not create a scope.
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        /// <summary>
        /// Gets the shared no-op scope instance.
        /// </summary>
        public static NullScope Instance { get; } = new();

        /// <summary>
        /// Disposes the no-op scope.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
