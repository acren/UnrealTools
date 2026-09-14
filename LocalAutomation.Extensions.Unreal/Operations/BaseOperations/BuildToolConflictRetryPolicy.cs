using System;
using LocalAutomation.Runtime;
using SB.UnrealUtilities;

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

        /// <summary>Adapts attempt diagnostics to the independent Unreal contention classifier.</summary>
        private static bool Matches(ExecutionRetryContext context)
        {
            return UnrealFailureClassifier.IsBuildToolConflict(RetryFailureText.Build(context));
        }
    }
}
