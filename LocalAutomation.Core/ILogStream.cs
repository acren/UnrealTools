using System;
using System.Collections.Generic;
using Serilog.Events;

namespace LocalAutomation.Core;

/// <summary>
/// Exposes a buffered log stream that UI hosts can observe while operations are running.
/// </summary>
public interface ILogStream
{
    /// <summary>
    /// Raised when a new Serilog event is appended.
    /// </summary>
    event Action<LogEvent>? EntryAdded;

    /// <summary>
    /// Gets the current buffered Serilog events.
    /// </summary>
    IReadOnlyList<LogEvent> Entries { get; }

    /// <summary>
    /// Appends a new buffered Serilog event.
    /// </summary>
    void Add(LogEvent entry);

    /// <summary>
    /// Clears the buffered log events.
    /// </summary>
    void Clear();
}
