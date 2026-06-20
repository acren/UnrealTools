using System;
using LocalAutomation.Core;
using Microsoft.Extensions.Logging;

namespace UnrealAutomationCommon;

/// <summary>
/// Maintains the existing UnrealAutomationCommon logger type name while delegating to the unified application logger.
/// </summary>
public class EventLogger : ILogger
{
    /// <summary>
    /// Begins a logging scope on the unified application logger.
    /// </summary>
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return ApplicationLogger.Logger.BeginScope(state) ?? NullScope.Instance;
    }

    /// <summary>
    /// Reports whether the unified application logger accepts the provided severity.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel)
    {
        return ApplicationLogger.Logger.IsEnabled(logLevel);
    }

    /// <summary>
    /// Writes one log entry through the unified application logger.
    /// </summary>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ApplicationLogger.Logger.Log(logLevel, eventId, state, exception, formatter);
    }

    /// <summary>
    /// Provides a fallback no-op scope when the configured logger declines to create one.
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
