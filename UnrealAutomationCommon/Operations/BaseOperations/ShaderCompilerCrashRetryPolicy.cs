using System;
using LocalAutomation.Runtime;

namespace UnrealAutomationCommon.Operations.BaseOperations
{
    /// <summary>
    /// Owns retry behavior and detection for ShaderCompileWorker process crashes.
    /// </summary>
    internal static class ShaderCompilerCrashRetryPolicy
    {
        internal static ExecutionRetryPolicy Instance { get; } = new(
            maxAttempts: 2,
            shouldRetry: Matches,
            getDelay: context => TimeSpan.FromSeconds(1 << context.AttemptNumber));

        private static bool Matches(ExecutionRetryContext context)
        {
            string failureText = RetryFailureText.Build(context);
            return RetryFailureText.Contains(failureText, "ShaderCompileWorker failed")
                || RetryFailureText.Contains(failureText, "Crash inside the platform compiler");
        }
    }
}
