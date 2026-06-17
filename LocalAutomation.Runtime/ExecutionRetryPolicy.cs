using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace LocalAutomation.Runtime;

/// <summary>
/// Describes one log entry captured during a retry attempt.
/// </summary>
public readonly record struct ExecutionRetryLogEntry(LogLevel LogLevel, string Message);

/// <summary>
/// Carries the failed attempt data that a retry policy needs to decide whether another task-body attempt is safe.
/// </summary>
public sealed class ExecutionRetryContext
{
    /// <summary>
    /// Creates one failed-attempt context for either a failed operation result or an exception thrown by the task body.
    /// </summary>
    internal ExecutionRetryContext(
        int attemptNumber,
        int maxAttempts,
        OperationResult? result,
        Exception? exception,
        IReadOnlyList<ExecutionRetryLogEntry> logs)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt numbers are one-based.");
        }

        if (maxAttempts < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Retry policies require at least two attempts.");
        }

        if (attemptNumber > maxAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number cannot exceed the retry policy maximum.");
        }
        if ((result == null) == (exception == null))
        {
            throw new ArgumentException("Retry context requires exactly one failed result or exception.");
        }
        if (result?.Outcome == ExecutionTaskOutcome.Completed)
        {
            throw new ArgumentException("Successful results are not retry failures.", nameof(result));
        }

        AttemptNumber = attemptNumber;
        MaxAttempts = maxAttempts;
        Result = result;
        Exception = exception;
        Logs = (logs ?? throw new ArgumentNullException(nameof(logs))).ToArray();
    }

    /// <summary>
    /// Gets the one-based attempt number that just failed.
    /// </summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// Gets the maximum number of attempts allowed by the task retry policy.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// Gets the failed operation result when the task body returned a terminal failure.
    /// </summary>
    public OperationResult? Result { get; }

    /// <summary>
    /// Gets the exception thrown by the task body when the attempt failed by throwing.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets the logs captured while the failed attempt body was running.
    /// </summary>
    public IReadOnlyList<ExecutionRetryLogEntry> Logs { get; }
}

/// <summary>
/// Defines the retry eligibility and delay rules for an opt-in execution task.
/// </summary>
public sealed class ExecutionRetryPolicy
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(2);
    private readonly Func<ExecutionRetryContext, bool> _shouldRetry;
    private readonly Func<ExecutionRetryContext, TimeSpan> _getDelay;

    /// <summary>
    /// Creates one retry policy with a maximum attempt count, eligibility predicate, and optional delay selector.
    /// </summary>
    public ExecutionRetryPolicy(
        int maxAttempts,
        Func<ExecutionRetryContext, bool> shouldRetry,
        Func<ExecutionRetryContext, TimeSpan>? getDelay = null)
    {
        if (maxAttempts < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Retry policies require at least two attempts.");
        }

        MaxAttempts = maxAttempts;
        _shouldRetry = shouldRetry ?? throw new ArgumentNullException(nameof(shouldRetry));
        _getDelay = getDelay ?? (_ => DefaultDelay);
    }

    /// <summary>
    /// Gets the total number of attempts allowed, including the initial attempt.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// Returns whether the failed attempt should be retried before the policy reaches its final attempt.
    /// </summary>
    public bool ShouldRetry(ExecutionRetryContext context)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));
        return context.AttemptNumber < MaxAttempts && _shouldRetry(context);
    }

    /// <summary>
    /// Returns whether the predicate matches the failure, independent of the attempt-number gate.
    /// Used by <see cref="Or(ExecutionRetryPolicy[])"/> to identify which inner policies match
    /// without consuming their per-policy attempt budget.
    /// </summary>
    internal bool IsFailureRetryable(ExecutionRetryContext context)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));
        return _shouldRetry(context);
    }

    /// <summary>
    /// Returns the cancellation-aware delay that should run before the next attempt.
    /// </summary>
    public TimeSpan GetDelay(ExecutionRetryContext context)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));
        TimeSpan delay = _getDelay(context);
        if (delay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Retry delays cannot be negative.");
        }

        return delay;
    }

    /// <summary>
    /// Composes multiple retry policies with OR semantics. The returned policy retries when any inner policy
    /// matches the failure and has remaining attempt budget. Each inner policy tracks its own attempt count
    /// independently via <see cref="AsyncLocal{T}"/>, so a burst of failures matching one policy does not
    /// consume another policy's retry budget.
    /// </summary>
    /// <param name="policies">At least two retry policies to compose.</param>
    /// <exception cref="ArgumentException">Fewer than two policies.</exception>
    public static ExecutionRetryPolicy Or(params ExecutionRetryPolicy[] policies)
    {
        if (policies == null)
        {
            throw new ArgumentNullException(nameof(policies));
        }

        if (policies.Length < 2)
        {
            throw new ArgumentException("At least two retry policies are required.", nameof(policies));
        }

        int maxAttempts = policies.Max(p => p.MaxAttempts);
        AsyncLocal<int[]> policyAttempts = new();

        return new ExecutionRetryPolicy(
            maxAttempts: maxAttempts,
            shouldRetry: context =>
            {
                int[] counts = policyAttempts.Value;
                if (counts == null)
                {
                    counts = new int[policies.Length];
                    policyAttempts.Value = counts;
                }

                for (int i = 0; i < policies.Length; i++)
                {
                    if (!policies[i].IsFailureRetryable(context))
                    {
                        continue;
                    }

                    counts[i]++;

                    if (counts[i] < policies[i].MaxAttempts)
                    {
                        return true;
                    }
                }

                return false;
            },
            getDelay: context =>
            {
                TimeSpan maxDelay = TimeSpan.Zero;

                for (int i = 0; i < policies.Length; i++)
                {
                    if (!policies[i].IsFailureRetryable(context))
                    {
                        continue;
                    }

                    TimeSpan current = policies[i].GetDelay(context);
                    if (current > maxDelay)
                    {
                        maxDelay = current;
                    }
                }

                return maxDelay > TimeSpan.Zero
                    ? maxDelay
                    : policies[0].GetDelay(context);
            });
    }
}
