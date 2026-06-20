using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace LocalAutomation.Runtime;

/// <summary>
/// Maps Serilog event severities to the Microsoft log-level model still used by some callers.
/// </summary>
public static class LogLevelInterop
{
    /// <summary>
    /// Converts one Serilog event level into the equivalent Microsoft log level.
    /// </summary>
    public static LogLevel ToMicrosoftLogLevel(LogEventLevel logEventLevel)
    {
        return logEventLevel switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            LogEventLevel.Fatal => LogLevel.Critical,
            _ => LogLevel.None
        };
    }
}
