using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalAutomation.Runtime;

/// <summary>
/// Evaluates how much unfinished downstream work one concrete next-startable task can unlock for scheduler ordering and
/// execution-lock admission.
/// </summary>
internal sealed class DownstreamWorkScorer
{
    // The scorer reads the live task graph from the active session so priority reflects the current runtime state.
    private readonly ExecutionSession _session;

    /// <summary>
    /// Creates one scorer over the provided live execution session.
    /// </summary>
    internal DownstreamWorkScorer(ExecutionSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// Computes the scheduler priority evidence for one concrete next-startable task.
    /// </summary>
    internal DownstreamWorkScore Evaluate(ExecutionTask task)
    {
        if (task == null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        HashSet<ExecutionTaskId> countedTaskIds = CollectDownstreamWorkTaskIds(task);
        return new DownstreamWorkScore(countedTaskIds.Count, countedTaskIds);
    }

    /// <summary>
    /// Collects the exact unfinished downstream tasks that contribute to the scheduler's downstream-work score.
    /// </summary>
    private HashSet<ExecutionTaskId> CollectDownstreamWorkTaskIds(ExecutionTask task)
    {
        // Counted tasks are the finished scoring result exposed to the scheduler and diagnostics.
        HashSet<ExecutionTaskId> countedTaskIds = new();
        // Controlled branch tasks are the contender path and promoted scopes whose remaining subtree belongs to that path.
        HashSet<ExecutionTaskId> controlledBranchTaskIds = new() { task.Id };
        // Queued task ids keep the reverse dependency walk stable and one-pass even when multiple paths converge.
        HashSet<ExecutionTaskId> queuedTaskIds = new();
        // Expanded subtree roots prevent rescanning the same proven downstream subtree under multiple promoted scopes.
        HashSet<ExecutionTaskId> expandedSubtreeRootTaskIds = new();
        CollectDownstreamDependentTasks(task, countedTaskIds, controlledBranchTaskIds, queuedTaskIds, expandedSubtreeRootTaskIds);
        return countedTaskIds;
    }

    /// <summary>
    /// Returns whether every remaining non-terminal task beneath the supplied ancestor is already on the contender path
    /// or already proven downstream of it.
    /// </summary>
    private static bool IsAncestorSubtreeControlledByBranch(
        ExecutionTask ancestorTask,
        HashSet<ExecutionTaskId> controlledBranchTaskIds,
        HashSet<ExecutionTaskId> countedTaskIds)
    {
        // The breadth-first walk checks the ancestor's current live subtree, not authored structure from an earlier phase.
        Queue<ExecutionTask> pendingTasks = new(ancestorTask.Children.Where(child => child.State != ExecutionTaskState.Completed));
        while (pendingTasks.Count > 0)
        {
            ExecutionTask currentTask = pendingTasks.Dequeue();
            if (!controlledBranchTaskIds.Contains(currentTask.Id) && !countedTaskIds.Contains(currentTask.Id))
            {
                return false;
            }

            foreach (ExecutionTask childTask in currentTask.Children.Where(child => child.State != ExecutionTaskState.Completed))
            {
                pendingTasks.Enqueue(childTask);
            }
        }

        return true;
    }

    /// <summary>
    /// Walks unfinished dependency edges outward from the concrete next-running task and accumulates every transitively
    /// downstream task into the provided counted set.
    /// </summary>
    private void CollectDownstreamDependentTasks(
        ExecutionTask task,
        HashSet<ExecutionTaskId> countedTaskIds,
        HashSet<ExecutionTaskId> controlledBranchTaskIds,
        HashSet<ExecutionTaskId> queuedTaskIds,
        HashSet<ExecutionTaskId> expandedSubtreeRootTaskIds)
    {
        // Pending task ids drive the reverse dependency walk from one proven downstream prerequisite to the next.
        Queue<ExecutionTaskId> pendingTaskIds = new();
        EnqueueTaskForDependencyTraversal(task.Id, queuedTaskIds, pendingTaskIds);
        PromoteControlledAncestorScopes(task, countedTaskIds, controlledBranchTaskIds, queuedTaskIds, pendingTaskIds, expandedSubtreeRootTaskIds);

        while (pendingTaskIds.Count > 0)
        {
            ExecutionTaskId currentTaskId = pendingTaskIds.Dequeue();
            foreach (ExecutionTask dependentTask in _session.Tasks.Where(candidate => candidate.Outcome == null && candidate.Dependencies.Contains(currentTaskId)))
            {
                PromoteControlledTaskAndAncestorScopes(
                    dependentTask,
                    countedTaskIds,
                    controlledBranchTaskIds,
                    queuedTaskIds,
                    pendingTaskIds,
                    expandedSubtreeRootTaskIds);
            }
        }
    }

    /// <summary>
    /// Adds one discovered downstream task to the counted set, queues it for further dependency traversal, and then lifts
    /// that branch through any ancestor scopes whose remaining non-terminal subtree is already controlled by the same
    /// contender branch.
    /// </summary>
    private static void PromoteControlledTaskAndAncestorScopes(
        ExecutionTask task,
        HashSet<ExecutionTaskId> countedTaskIds,
        HashSet<ExecutionTaskId> controlledBranchTaskIds,
        HashSet<ExecutionTaskId> queuedTaskIds,
        Queue<ExecutionTaskId> pendingTaskIds,
        HashSet<ExecutionTaskId> expandedSubtreeRootTaskIds)
    {
        controlledBranchTaskIds.Add(task.Id);
        if (countedTaskIds.Add(task.Id))
        {
            EnqueueTaskForDependencyTraversal(task.Id, queuedTaskIds, pendingTaskIds);
        }

        EnqueueDownstreamDescendants(task, countedTaskIds, queuedTaskIds, pendingTaskIds, expandedSubtreeRootTaskIds);
        PromoteControlledAncestorScopes(task, countedTaskIds, controlledBranchTaskIds, queuedTaskIds, pendingTaskIds, expandedSubtreeRootTaskIds);
    }

    /// <summary>
    /// Promotes ancestor scopes only while the contender branch already owns every remaining non-terminal task beneath
    /// that ancestor.
    /// </summary>
    private static void PromoteControlledAncestorScopes(
        ExecutionTask task,
        HashSet<ExecutionTaskId> countedTaskIds,
        HashSet<ExecutionTaskId> controlledBranchTaskIds,
        HashSet<ExecutionTaskId> queuedTaskIds,
        Queue<ExecutionTaskId> pendingTaskIds,
        HashSet<ExecutionTaskId> expandedSubtreeRootTaskIds)
    {
        for (ExecutionTask? currentTask = task.Parent;
             currentTask != null && IsAncestorSubtreeControlledByBranch(currentTask, controlledBranchTaskIds, countedTaskIds);
             currentTask = currentTask.Parent)
        {
            controlledBranchTaskIds.Add(currentTask.Id);
            if (countedTaskIds.Add(currentTask.Id))
            {
                EnqueueTaskForDependencyTraversal(currentTask.Id, queuedTaskIds, pendingTaskIds);
            }

            EnqueueDownstreamDescendants(currentTask, countedTaskIds, queuedTaskIds, pendingTaskIds, expandedSubtreeRootTaskIds);
        }
    }

    /// <summary>
    /// Queues one proven downstream task id only once for explicit dependent traversal.
    /// </summary>
    private static void EnqueueTaskForDependencyTraversal(
        ExecutionTaskId taskId,
        HashSet<ExecutionTaskId> queuedTaskIds,
        Queue<ExecutionTaskId> pendingTaskIds)
    {
        if (queuedTaskIds.Add(taskId))
        {
            pendingTaskIds.Enqueue(taskId);
        }
    }

    /// <summary>
    /// Adds unfinished descendants only for a task or scope that is already proven downstream.
    /// </summary>
    private static void EnqueueDownstreamDescendants(
        ExecutionTask task,
        HashSet<ExecutionTaskId> countedTaskIds,
        HashSet<ExecutionTaskId> queuedTaskIds,
        Queue<ExecutionTaskId> pendingTaskIds,
        HashSet<ExecutionTaskId> expandedSubtreeRootTaskIds)
    {
        if (!expandedSubtreeRootTaskIds.Add(task.Id))
        {
            return;
        }

        // The descendant expansion counts whole proven downstream scopes without inflating unrelated sibling scopes.
        Queue<ExecutionTask> pendingDescendants = new(task.Children.Where(child => child.Outcome == null));
        while (pendingDescendants.Count > 0)
        {
            ExecutionTask descendantTask = pendingDescendants.Dequeue();
            expandedSubtreeRootTaskIds.Add(descendantTask.Id);
            if (countedTaskIds.Add(descendantTask.Id))
            {
                EnqueueTaskForDependencyTraversal(descendantTask.Id, queuedTaskIds, pendingTaskIds);
            }

            foreach (ExecutionTask childTask in descendantTask.Children.Where(child => child.Outcome == null))
            {
                pendingDescendants.Enqueue(childTask);
            }
        }
    }
}

/// <summary>
/// Carries the downstream-work count together with the exact counted task ids used to derive that count.
/// </summary>
internal readonly record struct DownstreamWorkScore(int Count, IReadOnlyCollection<ExecutionTaskId> CountedTaskIds);
