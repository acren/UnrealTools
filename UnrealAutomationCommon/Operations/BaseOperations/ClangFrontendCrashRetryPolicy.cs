using System;
using LocalAutomation.Runtime;

namespace UnrealAutomationCommon.Operations.BaseOperations
{
    /// <summary>
    /// Owns retry behavior and detection for Clang frontend process crashes.
    /// </summary>
    internal static class ClangFrontendCrashRetryPolicy
    {
        internal static ExecutionRetryPolicy Instance { get; } = new(
            maxAttempts: 2,
            shouldRetry: Matches,
            getDelay: context => TimeSpan.FromSeconds(1 << context.AttemptNumber));

        private static bool Matches(ExecutionRetryContext context)
        {
            string failureText = RetryFailureText.Build(context);
            return RetryFailureText.Contains(failureText, "PLEASE submit a bug report to https://github.com/llvm/llvm-project/issues/")
                && RetryFailureText.Contains(failureText, "clang frontend command failed due to signal");
        }
    }
}
