using System;
using LocalAutomation.Runtime;

namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations
{
    /// <summary>
    /// Owns retry behavior and detection for UBT/UAT contention on shared build-tool state.
    /// </summary>
    internal static class BuildToolConflictRetryPolicy
    {
        internal static ExecutionRetryPolicy Instance { get; } = new(
            maxAttempts: 5,
            shouldRetry: Matches,
            getDelay: context => TimeSpan.FromSeconds(1 << context.AttemptNumber));

        private static bool Matches(ExecutionRetryContext context)
        {
            string failureText = RetryFailureText.Build(context);
            bool sharedBuildRulesLock = RetryFailureText.Contains(failureText, "BuildRules")
                && RetryFailureText.Contains(failureText, "MarketplaceRules.dll")
                && WindowsSharingViolationFailure.Matches(failureText);
            return sharedBuildRulesLock
                || RetryFailureText.Contains(failureText, "A conflicting instance of UnrealBuildTool is already running");
        }
    }
}
