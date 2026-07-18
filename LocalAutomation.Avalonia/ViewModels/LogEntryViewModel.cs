using System;
using LocalAutomation.Runtime;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace LocalAutomation.Avalonia.ViewModels;

/// <summary>
/// Represents a single log line rendered by the Avalonia execution output panel.
/// </summary>
public sealed class LogEntryViewModel
{
    /// <summary>
    /// Creates a UI log entry from the formatted message and severity.
    /// </summary>
    public LogEntryViewModel(string message, LogLevel verbosity, DateTimeOffset? timestamp = null)
    {
        Message = message;
        Verbosity = verbosity;
        Timestamp = timestamp ?? DateTimeOffset.Now;
    }

    /// <summary>
    /// Creates a UI log entry by projecting one raw Serilog event at the Avalonia edge.
    /// </summary>
    public LogEntryViewModel(LogEvent logEvent)
        : this(RenderMessage(logEvent), LogLevelInterop.ToMicrosoftLogLevel(logEvent.Level), logEvent.Timestamp)
    {
        ExecutionTaskId = ExecutionSessionLog.TryGetTaskId(logEvent, out ExecutionTaskId taskId)
            ? taskId
            : null;
    }

    /// <summary>
    /// Gets the execution task that emitted this row when the source event belongs to a task.
    /// </summary>
    public ExecutionTaskId? ExecutionTaskId { get; }

    /// <summary>
    /// Gets the formatted message text.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the local timestamp captured when the log line was added to the UI.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the formatted timestamp prefix shown before the log text.
    /// </summary>
    public string TimestampText => Timestamp.ToString("HH:mm:ss");

    /// <summary>
    /// Gets the severity for styling.
    /// </summary>
    public LogLevel Verbosity { get; }

    /// <summary>
    /// Gets whether the log line should be styled as an error.
    /// </summary>
    public bool IsError => Verbosity >= LogLevel.Error;

    /// <summary>
    /// Gets whether the log line should be styled as a warning.
    /// </summary>
    public bool IsWarning => Verbosity == LogLevel.Warning;

    /// <summary>
    /// Gets the foreground color used by the output list.
    /// </summary>
    public string Foreground
    {
        get
        {
            if (IsError)
            {
                return "#E65050";
            }

            if (IsWarning)
            {
                return "#E6E60A";
            }

            return "#E6E6E6";
        }
    }

    /// <summary>
    /// Renders the message text shown for one raw Serilog event.
    /// </summary>
    private static string RenderMessage(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        string message = logEvent.RenderMessage() ?? string.Empty;
        if (logEvent.Exception == null)
        {
            return message;
        }

        return string.IsNullOrWhiteSpace(message)
            ? logEvent.Exception.ToString()
            : message + Environment.NewLine + logEvent.Exception;
    }
}
