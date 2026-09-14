using System;
using LocalAutomation.Runtime;
using SB.SystemUtilities.Diagnostics;

namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations
{
    /// <summary>
    /// Owns bounded retry behavior for C++ compiler frontend crashes and internal compiler errors.
    /// </summary>
    internal static class CppCompilerCrashRetryPolicy
    {
        /// <summary>
        /// Gets the retry policy shared by native C++ build phases.
        /// </summary>
        internal static ExecutionRetryPolicy Instance { get; } = new(
            maxAttempts: 2,
            shouldRetry: Matches,
            getDelay: context => TimeSpan.FromSeconds(1 << context.AttemptNumber));

        /// <summary>
        /// Returns whether the captured failure contains an exact supported compiler failure signature.
        /// </summary>
        private static bool Matches(ExecutionRetryContext context)
        {
            return CppCompilerFailure.Matches(RetryFailureText.Build(context));
        }
    }
}
