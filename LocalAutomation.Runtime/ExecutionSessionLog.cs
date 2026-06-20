using System;
using System.Collections.Generic;
using System.Linq;
using LocalAutomation.Core;

namespace LocalAutomation.Runtime;

/// <summary>
/// Owns buffered session-log access, per-task log fanout, and task-scoped log queries for one execution session.
/// </summary>
public sealed class ExecutionSessionLog
{
    private readonly ExecutionSession _session;

    /// <summary>
    /// Creates the session-log owner around one execution session and its aggregate buffered stream.
    /// </summary>
    internal ExecutionSessionLog(ExecutionSession session, ILogStream stream)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Gets the aggregate buffered log stream for the whole execution session.
    /// </summary>
    public ILogStream Stream { get; }

    /// <summary>
    /// Appends one log entry to the aggregate session stream and the matching task-scoped stream.
    /// </summary>
    internal void Append(LogEntry entry)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        Stream.Add(entry);

        /* Session-level entries belong to the root task stream so root-scoped task views can consume the same whole-run
           output that appears in the aggregate session stream. */
        ExecutionTaskId? taskId = ExecutionTaskId.FromNullable(entry.TaskId);
        ExecutionTask scopedTask = taskId == null
            ? _session.RootTask
            : _session.GetTask(taskId.Value);
        scopedTask.LogStream.Add(entry);
    }

    /// <summary>
    /// Returns the buffered direct log stream for one task when that task exists in the session.
    /// </summary>
    public BufferedLogStream? GetTaskLogStream(ExecutionTaskId? taskId)
    {
        if (taskId == null)
        {
            return null;
        }

        /* Tree-walk lookup is acceptable here because task-log access is driven by UI interactions rather than the
           scheduler hot path, so keeping the lookup localized beats maintaining a second task index for logs alone. */
        return _session.Tasks.FirstOrDefault(task => task.Id == taskId.Value)?.LogStream;
    }

    /// <summary>
    /// Returns the aggregate session-log entries visible for one selected task-id set.
    /// </summary>
    public IReadOnlyList<LogEntry> GetScopedEntries(IReadOnlyCollection<ExecutionTaskId> selectedTaskIds)
    {
        if (selectedTaskIds == null)
        {
            throw new ArgumentNullException(nameof(selectedTaskIds));
        }

        List<LogEntry> sessionEntries = Stream.Entries.ToList();
        if (selectedTaskIds.Count == 0)
        {
            return sessionEntries;
        }

        HashSet<ExecutionTaskId> selectedTaskIdSet = new(selectedTaskIds);
        bool selectedScopeIncludesRoot = selectedTaskIdSet.Contains(_session.RootTask.Id);
        return sessionEntries
            .Where(entry =>
            {
                ExecutionTaskId? taskId = ExecutionTaskId.FromNullable(entry.TaskId);
                // Session-level entries describe the whole run rather than one task, so only root-containing scopes include them.
                return taskId == null
                    ? selectedScopeIncludesRoot
                    : selectedTaskIdSet.Contains(taskId.Value);
            })
            .ToList();
    }

    /// <summary>
    /// Returns the aggregate session-log entries visible for one task subtree.
    /// </summary>
    public IReadOnlyList<LogEntry> GetTaskScopedEntries(ExecutionTaskId taskId)
    {
        IReadOnlyList<ExecutionTaskId> selectedTaskIds = _session.GetTaskSubtreeIds(taskId);
        return GetScopedEntries(selectedTaskIds);
    }
}
