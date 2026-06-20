using System;
using LocalAutomation.Core;
using Serilog.Core;
using Serilog.Events;

namespace LocalAutomation.Avalonia;

/// <summary>
/// Appends raw Serilog events from the unified logging pipeline into the buffered application log stream.
/// </summary>
internal sealed class ApplicationLogBufferSink : ILogEventSink
{
    private readonly BufferedLogStream _logStream;

    /// <summary>
    /// Creates a buffered application-log sink for the provided application stream.
    /// </summary>
    public ApplicationLogBufferSink(BufferedLogStream logStream)
    {
        _logStream = logStream ?? throw new ArgumentNullException(nameof(logStream));
    }

    /// <summary>
    /// Appends one raw Serilog event to the buffered application log stream.
    /// </summary>
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        _logStream.Add(logEvent);
    }
}
