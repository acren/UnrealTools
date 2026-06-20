using System;
using System.IO;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace LocalAutomation.Application;

/// <summary>
/// Owns the canonical Serilog text formatter reused by every non-UI log export in the solution.
/// </summary>
public static class UnifiedLogTextFormatting
{
    private static readonly ITextFormatter SharedFormatter = new MessageTemplateTextFormatter(
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {TaskId} {Message:lj}{NewLine}{Exception}");

    /// <summary>
    /// Gets the canonical formatter used for launch logs, session log files, and graph task log exports.
    /// </summary>
    public static ITextFormatter Formatter => SharedFormatter;

    /// <summary>
    /// Writes one Serilog event through the canonical non-UI text formatter.
    /// </summary>
    public static void Format(TextWriter writer, LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(logEvent);
        SharedFormatter.Format(logEvent, writer);
    }
}
