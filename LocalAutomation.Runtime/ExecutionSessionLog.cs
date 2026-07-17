using System;
using System.Collections.Generic;
using System.Linq;
using LocalAutomation.Core;
using Serilog.Core;
using Serilog.Events;

namespace LocalAutomation.Runtime;

/// <summary>
/// Owns per-session Serilog event ingress, storage, task-scoped buffered publication, and subtree scoping.
/// </summary>
public sealed class ExecutionSessionLog : ILogEventSink
{
    private const string TaskIdPropertyName = "TaskId";

    private readonly List<LogEvent> _events = new();
    private readonly object _syncRoot = new();
    private readonly ExecutionSession _session;

    /// <summary>
    /// Creates the session-log owner around one execution session and its aggregate buffered event stream.
    /// </summary>
    internal ExecutionSessionLog(ExecutionSession session, ILogStream stream)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Raised whenever a Serilog event is appended to this session log.
    /// </summary>
    public event Action<LogEvent>? EventAdded;

    /// <summary>
    /// Gets the aggregate buffered event stream for the whole execution session.
    /// </summary>
    public ILogStream Stream { get; }

    /// <summary>
    /// Gets the execution-session identifier used to route Serilog events into this session log.
    /// </summary>
    public ExecutionSessionId SessionId => _session.Id;

    /// <summary>
    /// Accepts one session-owned Serilog event through the direct sink ingress, mirrors it into aggregate and task-scoped
    /// buffers, and notifies subscribers.
    /// </summary>
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        lock (_syncRoot)
        {
            _events.Add(logEvent);
        }

        Stream.Add(logEvent);

        /* Session-level events belong to the root task stream so root-scoped views include the same whole-run output as
           the aggregate session stream. */
        ExecutionTask scopedTask = TryGetTaskId(logEvent, out ExecutionTaskId taskId)
            ? _session.GetTask(taskId)
            : _session.RootTask;
        scopedTask.LogStream.Add(logEvent);
        _session.ApplyLogMetrics(logEvent);

        EventAdded?.Invoke(logEvent);
    }

    /// <summary>
    /// Returns the buffered direct event stream for one task when that task exists in the session.
    /// </summary>
    public BufferedLogStream? GetTaskLogStream(ExecutionTaskId? taskId)
    {
        if (taskId == null)
        {
            return null;
        }

        return _session.Tasks.FirstOrDefault(task => task.Id == taskId.Value)?.LogStream;
    }

    /// <summary>
    /// Clears the stored session events and the buffered task streams derived from them.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _events.Clear();
        }

        Stream.Clear();
        foreach (ExecutionTask task in _session.Tasks)
        {
            task.LogStream.Clear();
        }
    }

    /// <summary>
    /// Returns whether one event belongs to the selected task scope, including session-level events in the root scope.
    /// </summary>
    public bool IsEventInScope(LogEvent logEvent, IReadOnlyCollection<ExecutionTaskId> selectedTaskIds)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(selectedTaskIds);

        if (selectedTaskIds.Count == 0)
        {
            return true;
        }

        return TryGetTaskId(logEvent, out ExecutionTaskId taskId)
            ? selectedTaskIds.Contains(taskId)
            : selectedTaskIds.Contains(_session.RootTask.Id);
    }

    /// <summary>
    /// Returns Serilog events visible for one selected task-id set.
    /// </summary>
    public IReadOnlyList<LogEvent> GetScopedEvents(IReadOnlyCollection<ExecutionTaskId> selectedTaskIds)
    {
        ArgumentNullException.ThrowIfNull(selectedTaskIds);

        LogEvent[] sessionEvents;
        lock (_syncRoot)
        {
            sessionEvents = _events.ToArray();
        }

        if (selectedTaskIds.Count == 0)
        {
            return sessionEvents;
        }

        HashSet<ExecutionTaskId> selectedTaskIdSet = new(selectedTaskIds);
        return sessionEvents
            .Where(logEvent => IsEventInScope(logEvent, selectedTaskIdSet))
            .ToList();
    }

    /// <summary>
    /// Returns Serilog events visible for one task subtree.
    /// </summary>
    public IReadOnlyList<LogEvent> GetTaskScopedEvents(ExecutionTaskId taskId)
    {
        IReadOnlyList<ExecutionTaskId> selectedTaskIds = _session.GetTaskSubtreeIds(taskId);
        return GetScopedEvents(selectedTaskIds);
    }

    /// <summary>
    /// Reads the structured task id property from one Serilog event when it is present.
    /// </summary>
    public static bool TryGetTaskId(LogEvent logEvent, out ExecutionTaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        taskId = default;
        if (!TryGetScalarString(logEvent, TaskIdPropertyName, out string? taskIdValue) || taskIdValue == null)
        {
            return false;
        }

        taskId = new ExecutionTaskId(taskIdValue);
        return true;
    }

    /// <summary>
    /// Reads one structured scalar string property from the event when present.
    /// </summary>
    internal static bool TryGetScalarString(LogEvent logEvent, string propertyName, out string? value)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        value = null;
        if (!logEvent.Properties.TryGetValue(propertyName, out LogEventPropertyValue? propertyValue))
        {
            return false;
        }

        if (propertyValue is ScalarValue { Value: string stringValue } && !string.IsNullOrWhiteSpace(stringValue))
        {
            value = stringValue;
            return true;
        }

        return false;
    }

}
