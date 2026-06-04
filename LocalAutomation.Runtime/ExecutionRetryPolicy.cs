using System;
using System.Collections.Generic;
using System.Linq;
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
}
