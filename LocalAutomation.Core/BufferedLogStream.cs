using System;
using System.Collections.Generic;
using Serilog.Events;

namespace LocalAutomation.Core;

/// <summary>
/// Stores Serilog events in memory and notifies subscribers as new output arrives.
/// </summary>
public sealed class BufferedLogStream : ILogStream
{
    private readonly List<LogEvent> _entries = new();
    private readonly object _syncRoot = new();

    /// <summary>
    /// Raised whenever a new Serilog event is appended.
    /// </summary>
    public event Action<LogEvent>? EntryAdded;

    /// <summary>
    /// Gets the current buffered Serilog events.
    /// </summary>
    public IReadOnlyList<LogEvent> Entries
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
    /// Appends a new Serilog event and notifies subscribers.
    /// </summary>
    public void Add(LogEvent entry)
    {
        lock (_syncRoot)
        {
            _entries.Add(entry);
        }

        EntryAdded?.Invoke(entry);
    }

    /// <summary>
    /// Clears the buffered log events.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
        }
    }
}
