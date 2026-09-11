using System;
using LocalAutomation.Runtime;
using SystemUtilities.Diagnostics;

namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations
{
    /// <summary>
    /// Owns retry behavior and detection for workspace mutation sharing violations.
    /// </summary>
    internal static class WorkspaceFileLockRetryPolicy
    {
        internal static ExecutionRetryPolicy Instance { get; } = new(
            maxAttempts: 5,
            shouldRetry: Matches,
            getDelay: context => TimeSpan.FromSeconds(1 << context.AttemptNumber));

        /// <summary>
        /// Classifies the complete attempt evidence while the runtime policy owns the retry budget.
        /// </summary>
        private static bool Matches(ExecutionRetryContext context)
        {
            return WindowsSharingViolationFailure.Matches(RetryFailureText.Build(context));
        }
    }
}
