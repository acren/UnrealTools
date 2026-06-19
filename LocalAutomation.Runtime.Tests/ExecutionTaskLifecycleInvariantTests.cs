using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace LocalAutomation.Runtime.Tests;

public sealed class ExecutionTaskLifecycleInvariantTests
{
    /// <summary>
    /// Confirms that a completed task's cached duration remains frozen at its completion boundary instead of advancing
    /// when callers ask for metrics with a later clock value.
    /// </summary>
    [Fact]
    public async Task CompletedTaskDurationDoesNotAdvanceAfterCompletion()
    {
        ExecutionTaskId timedTaskId = default;

        /* A single runnable task is the smallest runtime shape that exercises the start timestamp, completion timestamp,
           and cached subtree timing basis used by task metric reads. */
        Operation operation = new RuntimeTestUtilities.InlineOperation(root =>
        {
            root.Children(scope =>
            {
                scope.Task("Timed Work").Run(() => Task.CompletedTask, out timedTaskId);
            });
        });

        /* Execute through the real scheduler so the task reaches Running and then Completed through normal runtime
           transitions before the duration is read back. */
        (ExecutionPlan _, ExecutionSession session, OperationResult result) = await RuntimeTestUtilities.ExecuteAsync(operation);
        ExecutionTask timedTask = session.GetTask(timedTaskId);
        DateTimeOffset finishedAt = timedTask.FinishedAt ?? throw new InvalidOperationException("The timed task did not record a finish timestamp.");

        /* The task completed successfully, so any later metric read should reuse the recorded finish timestamp instead of
           treating the completed task as still actively timed. */
        TimeSpan? durationAtCompletion = session.GetTaskDuration(timedTaskId, finishedAt);
        TimeSpan? durationAfterCompletion = session.GetTaskDuration(timedTaskId, finishedAt + TimeSpan.FromMinutes(10));

        Assert.Equal(ExecutionTaskOutcome.Completed, result.Outcome);
        Assert.Equal(ExecutionTaskState.Completed, timedTask.State);
        Assert.NotNull(durationAtCompletion);
        Assert.Equal(durationAtCompletion, durationAfterCompletion);
    }

    /// <summary>
    /// Confirms that once a scope has started and its only prerequisite has completed, the scope should surface
    /// WaitingForDependencies when its remaining reachable work is blocked only by an unfinished task outside the scope.
    /// </summary>
    [Fact]
    public async Task StartedScopeWithSatisfiedPrerequisiteWaitsForDependencies()
    {
        /* Keep one unrelated task running so the blocked child stays non-terminal after the scope already started. */
        TaskCompletionSource<bool> releaseBlocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecutionTaskId blockerTaskId = default;
        ExecutionTaskId prepareSharedSourceTaskId = default;
        ExecutionTaskId startedScopeTaskId = default;
        ExecutionTaskId completedChildTaskId = default;

        /* The minimal shape for this invariant is:
           - one prerequisite,
           - one started scope that depends only on that prerequisite,
           - one completed child,
           - one later child still blocked on unrelated work. */
        Operation operation = new RuntimeTestUtilities.InlineOperation(root =>
        {
            root.Children(ExecutionChildMode.Parallel, scope =>
            {
                ExecutionTaskBuilder blocker = scope.Task("Blocker");
                blockerTaskId = blocker.Id;
                blocker.Run(async _ =>
                {
                    await releaseBlocker.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return OperationResult.Succeeded();
                });

                ExecutionTaskBuilder prerequisite = scope.Task("Prerequisite");
                prepareSharedSourceTaskId = prerequisite.Id;
                prerequisite.Run(() => Task.CompletedTask);

                ExecutionTaskBuilder startedScope = scope.Task("Started Scope");
                startedScopeTaskId = startedScope.Id;
                startedScope.After(prepareSharedSourceTaskId).Children(childScope =>
                {
                    ExecutionTaskBuilder completedChild = childScope.Task("Completed Child");
                    completedChildTaskId = completedChild.Id;
                    completedChild.Run(() => Task.CompletedTask);

                    childScope.Task("Blocked Child")
                        .After(blockerTaskId)
                        .Run(() => Task.CompletedTask);
                });
            });
        });

        /* Wait until the blocker is running and both prerequisite plus first child have already completed. That leaves
           the started scope non-terminal but momentarily idle, which is exactly the illegal post-start queued case. */
        (ExecutionPlan _, ExecutionSession session, ExecutionPlanScheduler scheduler) = RuntimeTestUtilities.CreateRuntime(operation);
        Task<OperationResult> executeTask = scheduler.ExecuteAsync(CancellationToken.None);
        try
        {
            await session.GetTask(blockerTaskId).WaitForStartAsync().WaitAsync(TimeSpan.FromSeconds(1));
            await session.GetTask(prepareSharedSourceTaskId).WaitForCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            await session.GetTask(completedChildTaskId).WaitForCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));

            /* The prerequisite is already terminal and the scope already started real descendant work, so the scope is no
               longer merely queued. With no local work still active, the remaining external blocker should surface as
               WaitingForDependencies. */
            Assert.Equal(ExecutionTaskOutcome.Completed, session.GetTask(prepareSharedSourceTaskId).Outcome);
            Assert.Equal(ExecutionTaskState.WaitingForDependencies, session.GetTask(startedScopeTaskId).State);
        }
        finally
        {
            /* Always release the blocker so a failing assertion does not strand the background scheduler run. */
            releaseBlocker.TrySetResult(true);
        }

        /* After cleanup releases the blocker, the run should still finish successfully. */
        OperationResult result = await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ExecutionTaskOutcome.Completed, result.Outcome);
    }

    /// <summary>
    /// Confirms that once a parent scope has started and one child already completed, the parent should surface
    /// WaitingForDependencies when its only remaining child work is blocked by an unfinished task outside that parent.
    /// </summary>
    [Fact]
    public async Task StartedParentScopeWaitsForDependenciesAfterChildCompletes()
    {
        /* Keep one unrelated task running so the second child remains blocked after the first child already finished. */
        TaskCompletionSource<bool> releaseBlocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecutionTaskId blockerTaskId = default;
        ExecutionTaskId parentScopeTaskId = default;
        ExecutionTaskId completedChildTaskId = default;

        /* This is the smallest shape for the parent-child invariant:
           - one parent scope,
           - one completed child,
           - one later child still blocked on unrelated work. */
        Operation operation = new RuntimeTestUtilities.InlineOperation(root =>
        {
            root.Children(ExecutionChildMode.Parallel, scope =>
            {
                ExecutionTaskBuilder blocker = scope.Task("Blocker");
                blockerTaskId = blocker.Id;
                blocker.Run(async _ =>
                {
                    await releaseBlocker.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return OperationResult.Succeeded();
                });

                ExecutionTaskBuilder parentScope = scope.Task("Parent Scope");
                parentScopeTaskId = parentScope.Id;
                parentScope.Children(childScope =>
                {
                    ExecutionTaskBuilder completedChild = childScope.Task("Completed Child");
                    completedChildTaskId = completedChild.Id;
                    completedChild.Run(() => Task.CompletedTask);

                    childScope.Task("Blocked Child")
                        .After(blockerTaskId)
                        .Run(() => Task.CompletedTask);
                });
            });
        });

        /* Wait until the blocker is running and the first child already completed. That leaves the parent scope started,
           non-terminal, and locally idle because its only remaining child work is blocked on an external dependency. */
        (ExecutionPlan _, ExecutionSession session, ExecutionPlanScheduler scheduler) = RuntimeTestUtilities.CreateRuntime(operation);
        Task<OperationResult> executeTask = scheduler.ExecuteAsync(CancellationToken.None);
        try
        {
            await session.GetTask(blockerTaskId).WaitForStartAsync().WaitAsync(TimeSpan.FromSeconds(1));
            await session.GetTask(completedChildTaskId).WaitForCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));

            /* The parent already has completed child work, so it is no longer merely queued. With no local running work
               left, its current blocker should surface as dependency wait instead of a generic running state. */
            Assert.Equal(ExecutionTaskOutcome.Completed, session.GetTask(completedChildTaskId).Outcome);
            Assert.Equal(ExecutionTaskState.WaitingForDependencies, session.GetTask(parentScopeTaskId).State);
        }
        finally
        {
            /* Always release the blocker so a failing assertion does not strand the background scheduler run. */
            releaseBlocker.TrySetResult(true);
        }

        /* After cleanup releases the blocker, the run should still finish successfully. */
        OperationResult result = await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ExecutionTaskOutcome.Completed, result.Outcome);
    }

    /// <summary>
    /// Confirms that a started parent stays dependency-waiting when its remaining child container is dependency-blocked,
    /// even if that blocked container has descendant work that would be locally ready after the container opens.
    /// </summary>
    [Fact]
    public async Task StartedParentScopeWaitsForDependenciesWhenBlockedChildContainerHasReadyDescendant()
    {
        /* Keep one unrelated task running so the child container remains blocked after the parent has already started. */
        TaskCompletionSource<bool> releaseBlocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecutionTaskId blockerTaskId = default;
        ExecutionTaskId parentScopeTaskId = default;
        ExecutionTaskId completedChildTaskId = default;

        /* This shape reproduces the nested blocked-container invariant: the parent has started through one completed
           child, while the remaining child container is dependency-blocked even though its own child has no dependencies. */
        Operation operation = new RuntimeTestUtilities.InlineOperation(root =>
        {
            root.Children(ExecutionChildMode.Parallel, scope =>
            {
                ExecutionTaskBuilder blocker = scope.Task("Blocker");
                blockerTaskId = blocker.Id;
                blocker.Run(async _ =>
                {
                    await releaseBlocker.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return OperationResult.Succeeded();
                });

                ExecutionTaskBuilder parentScope = scope.Task("Parent Scope");
                parentScopeTaskId = parentScope.Id;
                parentScope.Children(childScope =>
                {
                    ExecutionTaskBuilder completedChild = childScope.Task("Completed Child");
                    completedChildTaskId = completedChild.Id;
                    completedChild.Run(() => Task.CompletedTask);

                    childScope.Task("Blocked Child Container")
                        .After(blockerTaskId)
                        .Children(blockedScope =>
                        {
                            blockedScope.Task("Locally Ready Grandchild")
                                .Run(() => Task.CompletedTask);
                        });
                });
            });
        });

        /* Wait until the blocker is running and the first child already completed. That leaves the parent started with no
           active work, and its only remaining reachable child container is still blocked by the external dependency. */
        (ExecutionPlan _, ExecutionSession session, ExecutionPlanScheduler scheduler) = RuntimeTestUtilities.CreateRuntime(operation);
        Task<OperationResult> executeTask = scheduler.ExecuteAsync(CancellationToken.None);
        try
        {
            await session.GetTask(blockerTaskId).WaitForStartAsync().WaitAsync(TimeSpan.FromSeconds(1));
            await session.GetTask(completedChildTaskId).WaitForCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(ExecutionTaskState.WaitingForDependencies, session.GetTask(parentScopeTaskId).State);
        }
        finally
        {
            /* Always release the blocker so the background scheduler run can drain after the red-state assertion. */
            releaseBlocker.TrySetResult(true);
        }

        OperationResult result = await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ExecutionTaskOutcome.Completed, result.Outcome);
    }

    /// <summary>
    /// Confirms that a parent disabled with When(false, ...) keeps later-authored children terminal and disabled even
    /// while an external dependency changes state during execution.
    /// </summary>
    [Fact]
    public async Task DisabledParentScopeKeepsLaterAuthoredChildrenTerminalAcrossDependencyChanges()
    {
        /* Hold one unrelated task open so a disabled child can observe real dependency state changes without any work in
           the disabled subtree becoming runnable. */
        TaskCompletionSource<bool> releaseBlocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecutionTaskId blockerTaskId = default;
        ExecutionTaskId disabledParentTaskId = default;
        ExecutionTaskId immediateChildTaskId = default;
        ExecutionTaskId dependencyChildTaskId = default;

        /* Author the child scope only after When(false, ...) so the test covers the exact subtree-cascade contract that
           previously allowed a disabled parent to retain enabled descendants. */
        Operation operation = new RuntimeTestUtilities.InlineOperation(root =>
        {
            root.Children(ExecutionChildMode.Parallel, scope =>
            {
                ExecutionTaskBuilder blocker = scope.Task("Blocker");
                blockerTaskId = blocker.Id;
                blocker.Run(async _ =>
                {
                    await releaseBlocker.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    return OperationResult.Succeeded();
                });

                scope.Task("Disabled Parent Scope", out disabledParentTaskId)
                    .When(false, "Disabled parent scope.")
                    .Children(childScope =>
                    {
                        childScope.Task("Immediate Disabled Child")
                            .Run(() => Task.CompletedTask, out immediateChildTaskId);

                        childScope.Task("Dependency Disabled Child")
                            .After(blockerTaskId)
                            .Run(() => Task.CompletedTask, out dependencyChildTaskId);
                    });
            });
        });

        /* Build the live runtime before execution starts so the test can assert the terminal disabled contract at
           session initialization time, not only after the scheduler begins observing dependency changes. */
        (ExecutionPlan _, ExecutionSession session, ExecutionPlanScheduler scheduler) = RuntimeTestUtilities.CreateRuntime(operation);
        Assert.Equal(ExecutionTaskState.Completed, session.GetTask(disabledParentTaskId).State);
        Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(disabledParentTaskId).Outcome);
        Assert.Equal(ExecutionTaskState.Completed, session.GetTask(immediateChildTaskId).State);
        Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(immediateChildTaskId).Outcome);
        Assert.Equal(ExecutionTaskState.Completed, session.GetTask(dependencyChildTaskId).State);
        Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(dependencyChildTaskId).Outcome);

        /* Start the real scheduler, then wait until the unrelated blocker is actively running. The disabled dependency
           child must remain terminal throughout that external state transition. */
        Task<OperationResult> executeTask = scheduler.ExecuteAsync(CancellationToken.None);
        try
        {
            await session.GetTask(blockerTaskId).WaitForStartAsync().WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(ExecutionTaskState.Completed, session.GetTask(disabledParentTaskId).State);
            Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(disabledParentTaskId).Outcome);
            Assert.Equal(ExecutionTaskState.Completed, session.GetTask(immediateChildTaskId).State);
            Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(immediateChildTaskId).Outcome);
            Assert.Equal(ExecutionTaskState.Completed, session.GetTask(dependencyChildTaskId).State);
            Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(dependencyChildTaskId).Outcome);
        }
        finally
        {
            /* Always release the unrelated blocker so a failing assertion does not strand the background scheduler run. */
            releaseBlocker.TrySetResult(true);
        }

        /* Once the blocker completes, the disabled subtree should stay terminal and the overall run should still finish
           successfully without any completed-to-running transition attempt. */
        OperationResult result = await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ExecutionTaskOutcome.Completed, result.Outcome);
        Assert.Equal(ExecutionTaskState.Completed, session.GetTask(disabledParentTaskId).State);
        Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(disabledParentTaskId).Outcome);
        Assert.Equal(ExecutionTaskState.Completed, session.GetTask(immediateChildTaskId).State);
        Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(immediateChildTaskId).Outcome);
        Assert.Equal(ExecutionTaskState.Completed, session.GetTask(dependencyChildTaskId).State);
        Assert.Equal(ExecutionTaskOutcome.Disabled, session.GetTask(dependencyChildTaskId).Outcome);
    }
}
