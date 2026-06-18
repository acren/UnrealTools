using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LocalAutomation.Runtime;

/// <summary>
/// Provides one process-wide arbitration point for typed execution locks. Operations declare logical lock needs, and the
/// scheduler registers ready tasks here so each acquisition attempt can choose the best currently eligible waiter.
/// </summary>
internal static class ExecutionLocks
{
    // Protects the held-claim set and waiter list so lock grants are chosen from one coherent global view.
    private static readonly object _syncRoot = new();

    // Tracks lock claims currently owned by granted task executions or reservation-behavior task scopes.
    private static readonly List<LockClaim> _heldClaims = new();

    // Stores tasks that are eligible to run except for their declared lock claims.
    private static readonly List<LockWaiter> _waiters = new();

    // Gives equal-priority waiters a deterministic global FIFO tie-breaker.
    private static long _nextWaiterSequence;

    /// <summary>
    /// Fires after a lock-table change may let waiting schedulers retry acquisition.
    /// </summary>
    internal static event Action? Changed;

    /// <summary>
    /// Registers or refreshes one scheduler-ready task and atomically acquires all requested claim groups only when that
    /// ready task is the best eligible waiter. Each group receives its own release handle so task-owned scopes can end at
    /// their own lifecycle boundaries.
    /// </summary>
    internal static bool TryAcquireOrWait(
        ExecutionTaskId taskId,
        IReadOnlyList<(ExecutionTask Owner, IReadOnlyList<ExecutionTaskId> OwnerAncestry, ExecutionTaskBodyStatus BodyStatusOnGrant, IReadOnlyList<ExecutionLock> Locks)> executionLockGroups,
        int priority,
        out IReadOnlyList<IAsyncDisposable> handles)
    {
        handles = Array.Empty<IAsyncDisposable>();
        if (taskId == default)
        {
            throw new ArgumentException("A task id is required for lock wait registration.", nameof(taskId));
        }

        if (executionLockGroups == null)
        {
            throw new ArgumentNullException(nameof(executionLockGroups));
        }

        IReadOnlyList<IReadOnlyList<LockClaim>> requestedGroups = BuildRequestedClaimGroups(executionLockGroups);
        IReadOnlyList<LockClaim> requestedClaims = requestedGroups.SelectMany(group => group).ToList();
        if (requestedClaims.Count == 0)
        {
            return true;
        }

        ValidateRequestedClaims(requestedClaims);

        bool wakeWaiters = false;
        lock (_syncRoot)
        {
            int waiterIndex = _waiters.FindIndex(waiter => waiter.TaskId == taskId);
            long sequence = waiterIndex >= 0
                ? _waiters[waiterIndex].Sequence
                : Interlocked.Increment(ref _nextWaiterSequence);
            LockWaiter waiter = new(taskId, requestedClaims, priority, sequence);
            if (waiterIndex >= 0)
            {
                _waiters[waiterIndex] = waiter;
            }
            else
            {
                _waiters.Add(waiter);
            }

            LockWaiter? bestWaiter = _waiters
                .Where(candidate => SharesAnyKey(candidate.Claims, requestedClaims) && IsEligible(candidate))
                .OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Sequence)
                .Select(candidate => (LockWaiter?)candidate)
                .FirstOrDefault();
            if (bestWaiter?.TaskId == taskId)
            {
                _waiters.RemoveAll(candidate => candidate.TaskId == taskId);
                foreach (IReadOnlyList<LockClaim> group in requestedGroups)
                {
                    _heldClaims.AddRange(group);
                }

                handles = requestedGroups.Select(group => (IAsyncDisposable)new Releaser(group)).ToList();
                return true;
            }

            wakeWaiters = bestWaiter != null;
        }

        if (wakeWaiters)
        {
            SignalChanged();
        }

        return false;
    }

    /// <summary>
    /// Wakes waiters after an owning task changes body status while keeping the same granted lock claims.
    /// </summary>
    internal static void NotifyOwnerBodyStatusChanged()
    {
        SignalChanged();
    }

    /// <summary>
    /// Releases one previously granted lock handle through the same synchronous boundary the scheduler already uses for
    /// failed task admission. Lock handles only mutate the in-process lock table, so blocking here keeps release ordering
    /// deterministic without introducing fire-and-forget cleanup.
    /// </summary>
    internal static void ReleaseHandle(IAsyncDisposable handle)
    {
        _ = handle ?? throw new ArgumentNullException(nameof(handle));
        handle.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Removes one queued task from global lock arbitration.
    /// </summary>
    internal static void UnregisterWaiter(ExecutionTaskId taskId)
    {
        bool removed;
        lock (_syncRoot)
        {
            removed = _waiters.RemoveAll(waiter => waiter.TaskId == taskId) > 0;
        }

        if (removed)
        {
            SignalChanged();
        }
    }

    /// <summary>
    /// Normalizes grouped lock declarations into deterministic claim sets while preserving each release group.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<LockClaim>> BuildRequestedClaimGroups(
        IReadOnlyList<(ExecutionTask Owner, IReadOnlyList<ExecutionTaskId> OwnerAncestry, ExecutionTaskBodyStatus BodyStatusOnGrant, IReadOnlyList<ExecutionLock> Locks)> executionLockGroups)
    {
        List<IReadOnlyList<LockClaim>> requestedGroups = new();
        foreach ((ExecutionTask owner, IReadOnlyList<ExecutionTaskId> ownerAncestry, ExecutionTaskBodyStatus bodyStatusOnGrant, IReadOnlyList<ExecutionLock> locks) in executionLockGroups)
        {
            if (owner == null)
            {
                throw new InvalidOperationException("Execution lock owner task is required.");
            }

            IReadOnlyList<ExecutionTaskId> normalizedAncestry = NormalizeOwnerAncestry(owner.Id, ownerAncestry);
            IReadOnlyList<LockClaim> groupClaims = NormalizeExecutionLocks(locks)
                .Select(executionLock => new LockClaim(executionLock.Key, owner, normalizedAncestry, bodyStatusOnGrant))
                .ToList();
            if (groupClaims.Count > 0)
            {
                requestedGroups.Add(groupClaims);
            }
        }

        return requestedGroups;
    }

    /// <summary>
    /// Returns one deterministic lock list from a caller-authored lock set.
    /// </summary>
    private static IReadOnlyList<ExecutionLock> NormalizeExecutionLocks(IEnumerable<ExecutionLock> executionLocks)
    {
        _ = executionLocks ?? throw new ArgumentNullException(nameof(executionLocks));
        return executionLocks
            .Where(executionLock => executionLock != null)
            .DistinctBy(executionLock => executionLock.Key, StringComparer.Ordinal)
            .OrderBy(executionLock => executionLock.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Ensures the owner ancestry contains the owner id so descendant compatibility can be answered from a claim alone.
    /// </summary>
    private static IReadOnlyList<ExecutionTaskId> NormalizeOwnerAncestry(ExecutionTaskId ownerTaskId, IReadOnlyList<ExecutionTaskId>? ownerAncestry)
    {
        List<ExecutionTaskId> ancestry = ownerAncestry?.Where(taskId => taskId != default).Distinct().ToList() ?? new List<ExecutionTaskId>();
        if (!ancestry.Contains(ownerTaskId))
        {
            ancestry.Add(ownerTaskId);
        }

        return ancestry;
    }

    /// <summary>
    /// Rejects internally incompatible atomic requests before they can strand release handles with conflicting claims.
    /// </summary>
    private static void ValidateRequestedClaims(IReadOnlyList<LockClaim> requestedClaims)
    {
        for (int leftIndex = 0; leftIndex < requestedClaims.Count; leftIndex += 1)
        {
            for (int rightIndex = leftIndex + 1; rightIndex < requestedClaims.Count; rightIndex += 1)
            {
                if (!AreCompatible(requestedClaims[leftIndex], requestedClaims[rightIndex]))
                {
                    throw new InvalidOperationException($"Execution lock key '{requestedClaims[leftIndex].Key}' cannot be requested by incompatible scopes in the same acquisition.");
                }
            }
        }
    }

    /// <summary>
    /// Returns whether the waiter can run against all currently held claims.
    /// </summary>
    private static bool IsEligible(LockWaiter waiter)
    {
        return waiter.Claims.All(requested => _heldClaims.All(held => AreCompatible(requested, held)));
    }

    /// <summary>
    /// Returns whether two waiters belong to the same arbitration lane because they mention at least one common key.
    /// </summary>
    private static bool SharesAnyKey(IReadOnlyList<LockClaim> left, IReadOnlyList<LockClaim> right)
    {
        return left.Any(leftClaim => right.Any(rightClaim => string.Equals(leftClaim.Key, rightClaim.Key, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Applies the lock compatibility rule from owner body status: executing bodies conflict with all same-key claimants,
    /// while reservation-behavior owners admit same-key descendants and block outside claimants.
    /// </summary>
    private static bool AreCompatible(LockClaim left, LockClaim right)
    {
        if (!string.Equals(left.Key, right.Key, StringComparison.Ordinal))
        {
            return true;
        }

        bool leftReservationBehavior = HasReservationBehavior(left.CompatibilityStatus);
        bool rightReservationBehavior = HasReservationBehavior(right.CompatibilityStatus);
        if (!leftReservationBehavior && !rightReservationBehavior)
        {
            return false;
        }

        if (leftReservationBehavior && IsDescendantOf(right.OwnerTaskId, right.OwnerAncestry, left.OwnerTaskId))
        {
            return true;
        }

        return rightReservationBehavior && IsDescendantOf(left.OwnerTaskId, left.OwnerAncestry, right.OwnerTaskId);
    }

    /// <summary>
    /// Returns whether a body status makes declared locks block outsiders while allowing descendant active claims.
    /// </summary>
    private static bool HasReservationBehavior(ExecutionTaskBodyStatus bodyStatus)
    {
        return bodyStatus is ExecutionTaskBodyStatus.None or ExecutionTaskBodyStatus.WaitingForChild;
    }

    /// <summary>
    /// Returns whether the owner is a strict descendant of the supplied ancestor owner.
    /// </summary>
    private static bool IsDescendantOf(ExecutionTaskId ownerTaskId, IReadOnlyList<ExecutionTaskId> ownerAncestry, ExecutionTaskId ancestorTaskId)
    {
        return ownerTaskId != ancestorTaskId && ownerAncestry.Contains(ancestorTaskId);
    }

    /// <summary>
    /// Releases one granted claim set and wakes all known waiters so their schedulers can retry acquisition.
    /// </summary>
    private static void Release(Releaser releaser)
    {
        lock (_syncRoot)
        {
            foreach (LockClaim claim in releaser.Claims)
            {
                _heldClaims.Remove(claim);
            }
        }

        SignalChanged();
    }

    /// <summary>
    /// Notifies active schedulers outside the lock-table monitor so they can retry ready lock waiters.
    /// </summary>
    private static void SignalChanged()
    {
        Changed?.Invoke();
    }

    /// <summary>
    /// Owns one acquired claim set and returns those claims to the global coordinator when disposed.
    /// </summary>
    private sealed class Releaser(IReadOnlyList<LockClaim> claims) : IAsyncDisposable
    {
        private int _disposed;

        /// <summary>
        /// Gets the mutable claim objects this handle owns in the global held-claim table.
        /// </summary>
        internal IReadOnlyList<LockClaim> Claims { get; } = claims;

        /// <summary>
        /// Gets whether this release handle has already returned its claims to the global coordinator.
        /// </summary>
        internal bool IsDisposed => _disposed != 0;

        /// <summary>
        /// Releases the granted claims once, skipping notification for the shared lock-free path.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            if (Claims.Count > 0)
            {
                Release(this);
            }

            return default;
        }
    }

    /// <summary>
    /// Represents one scheduler-ready task that is blocked only by its requested execution-lock claims.
    /// </summary>
    private readonly record struct LockWaiter(
        ExecutionTaskId TaskId,
        IReadOnlyList<LockClaim> Claims,
        int Priority,
        long Sequence);

    /// <summary>
    /// Carries one normalized claim. Granted claims derive lock behavior from the owner task body status so compatibility
    /// cannot drift away from the task lifecycle.
    /// </summary>
    private sealed class LockClaim
    {
        public LockClaim(string key, ExecutionTask owner, IReadOnlyList<ExecutionTaskId> ownerAncestry, ExecutionTaskBodyStatus bodyStatusOnGrant)
        {
            Key = key;
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            OwnerAncestry = ownerAncestry;
            BodyStatusOnGrant = bodyStatusOnGrant;
        }

        public string Key { get; }

        public ExecutionTask Owner { get; }

        public ExecutionTaskId OwnerTaskId => Owner.Id;

        public IReadOnlyList<ExecutionTaskId> OwnerAncestry { get; }

        private ExecutionTaskBodyStatus BodyStatusOnGrant { get; }

        public ExecutionTaskBodyStatus CompatibilityStatus
        {
            get
            {
                ExecutionTaskBodyStatus currentStatus = Owner.BodyStatus;
                return currentStatus == ExecutionTaskBodyStatus.NotExecuting ? BodyStatusOnGrant : currentStatus;
            }
        }
    }
}
