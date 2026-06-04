using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LocalAutomation.Runtime;

/// <summary>
/// Runs an arbitrary asynchronous task body under an opt-in retry policy without depending on graph scheduling.
/// </summary>
internal static class ExecutionRetryExecutor
{
    /// <summary>
    /// Executes the attempt delegate until it succeeds, reaches a terminal non-retryable outcome, or exhausts the policy.
    /// </summary>
    public static async Task<OperationResult> ExecuteAsync(
        ExecutionRetryPolicy policy,
        string taskTitle,
        ILogger terminalLogger,
        CancellationToken cancellationToken,
        Func<ILogger, Task<OperationResult>> runAttemptAsync)
    {
        _ = policy ?? throw new ArgumentNullException(nameof(policy));
        _ = terminalLogger ?? throw new ArgumentNullException(nameof(terminalLogger));
        _ = runAttemptAsync ?? throw new ArgumentNullException(nameof(runAttemptAsync));

        string effectiveTaskTitle = string.IsNullOrWhiteSpace(taskTitle) ? "Execution task" : taskTitle;
        for (int attemptNumber = 1; attemptNumber <= policy.MaxAttempts; attemptNumber += 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RetryAttemptLogger attemptLogger = new(terminalLogger);
            ExecutionRetryContext retryContext;

            // Only the attempt body is inside this catch boundary; retry-delay cancellation must escape through the
            // ordinary cancellation path instead of being classified as an attempt-body failure.
            try
            {
                OperationResult result = await runAttemptAsync(attemptLogger).ConfigureAwait(false);
                if (IsTerminalWithoutRetry(result))
                {
                    return result;
                }

                retryContext = new(attemptNumber, policy.MaxAttempts, result, exception: null, attemptLogger.Entries);
                if (!policy.ShouldRetry(retryContext))
                {
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                retryContext = new(attemptNumber, policy.MaxAttempts, result: null, ex, attemptLogger.Entries);
                if (!policy.ShouldRetry(retryContext))
                {
                    throw;
                }
            }

            await DelayBeforeRetryAsync(policy, retryContext, terminalLogger, effectiveTaskTitle, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Retry policy for task '{effectiveTaskTitle}' did not reach a terminal attempt.");
    }

    /// <summary>
    /// Identifies outcomes that should never be retried by generic retry orchestration.
    /// </summary>
    private static bool IsTerminalWithoutRetry(OperationResult result)
    {
        return result.Outcome is ExecutionTaskOutcome.Completed or ExecutionTaskOutcome.Cancelled or ExecutionTaskOutcome.Interrupted;
    }

    /// <summary>
    /// Emits the retry notice, then waits before the next attempt.
    /// </summary>
    private static async Task DelayBeforeRetryAsync(
        ExecutionRetryPolicy policy,
        ExecutionRetryContext retryContext,
        ILogger terminalLogger,
        string taskTitle,
        CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = policy.GetDelay(retryContext);
        terminalLogger.LogInformation(
            "Task body '{TaskTitle}' attempt {AttemptNumber}/{MaxAttempts} failed with a retryable {FailureKind}; retrying in {RetryDelay}.",
            taskTitle,
            retryContext.AttemptNumber,
            retryContext.MaxAttempts,
            retryContext.Exception == null ? "result" : "exception",
            retryDelay);
        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
    }
}
