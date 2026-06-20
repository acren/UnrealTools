using System;
using LocalAutomation.Runtime;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace LocalAutomation.Application;

/// <summary>
/// Holds the live application-wide log visibility and file-output thresholds derived from persisted settings.
/// </summary>
public static class ApplicationLogThresholdSettings
{
    // Keep display visibility at the default UI threshold until settings load or the user changes it.
    private static LogLevel _displayMinimumLogLevel = LogLevel.Debug;
    // Keep file output at the historical default until settings load or the user changes it.
    private static LogLevel _fileMinimumLogLevel = LogLevel.Trace;

    /// <summary>
    /// Gets the minimum severity that remains visible in the UI log pane.
    /// </summary>
    public static LogLevel DisplayMinimumLogLevel => _displayMinimumLogLevel;

    /// <summary>
    /// Gets the minimum severity that is written to durable log files.
    /// </summary>
    public static LogLevel FileMinimumLogLevel => _fileMinimumLogLevel;

    /// <summary>
    /// Applies both current log thresholds from the durable application settings model.
    /// </summary>
    public static void Update(LogLevel displayMinimumLogLevel, LogLevel fileMinimumLogLevel)
    {
        _displayMinimumLogLevel = Normalize(displayMinimumLogLevel);
        _fileMinimumLogLevel = Normalize(fileMinimumLogLevel);
    }

    /// <summary>
    /// Returns whether the provided entry severity should remain visible in UI log panes.
    /// </summary>
    public static bool AllowsDisplay(LogLevel logLevel)
    {
        return Normalize(logLevel) >= _displayMinimumLogLevel;
    }

    /// <summary>
    /// Returns whether the provided Serilog event severity should remain visible in UI log panes.
    /// </summary>
    public static bool AllowsDisplay(LogEventLevel logLevel)
    {
        return AllowsDisplay(LogLevelInterop.ToMicrosoftLogLevel(logLevel));
    }

    /// <summary>
    /// Returns whether the provided entry severity should be written to durable log files.
    /// </summary>
    public static bool AllowsFileOutput(LogLevel logLevel)
    {
        return Normalize(logLevel) >= _fileMinimumLogLevel;
    }

    /// <summary>
    /// Returns whether the provided Serilog event severity should be written to durable log files.
    /// </summary>
    public static bool AllowsFileOutput(LogEventLevel logLevel)
    {
        return AllowsFileOutput(LogLevelInterop.ToMicrosoftLogLevel(logLevel));
    }

    /// <summary>
    /// Coerces undefined values back to a safe default so persisted corruption does not break logging.
    /// </summary>
    private static LogLevel Normalize(LogLevel logLevel)
    {
        return Enum.IsDefined(typeof(LogLevel), logLevel) ? logLevel : LogLevel.Trace;
    }

}
