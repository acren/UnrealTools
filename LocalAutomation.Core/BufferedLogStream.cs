using System;
using System.Collections.Generic;
using Serilog.Events;

namespace LocalAutomation.Core;

/// <summary>
/// Stores Serilog events in memory and notifies subscribers as new output arrives.
/// </summary>
public sealed class BufferedLogStream : ILogStream
{
    // Preserves insertion order for snapshots consumed by full log-pane rebuilds.
    private readonly List<LogEvent> _entries = new();
    // Provides constant-time reference membership for pending UI events that can overlap a clear.
    private readonly HashSet<LogEvent> _entrySet = new(ReferenceEqualityComparer.Instance);
    // Keeps ordered storage and membership synchronized as one authoritative buffer.
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
    /// Returns whether the exact event instance remains in the authoritative buffer.
    /// </summary>
    public bool Contains(LogEvent entry)
    {
        lock (_syncRoot)
        {
            return _entrySet.Contains(entry);
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
            _entrySet.Add(entry);
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
            _entrySet.Clear();
        }
    }
}
