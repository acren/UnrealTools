using System;
using LocalAutomation.Runtime;

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
            string failureText = RetryFailureText.Build(context);

            // Match vendor diagnostics rather than generic exit codes so deterministic compile failures remain terminal.
            bool clangFrontendCrash =
                RetryFailureText.Contains(failureText, "PLEASE submit a bug report to https://github.com/llvm/llvm-project/issues/")
                && RetryFailureText.Contains(failureText, "clang frontend command failed due to signal");
            bool msvcInternalCompilerError =
                RetryFailureText.Contains(failureText, "fatal error C1001: Internal compiler error");
            return clangFrontendCrash || msvcInternalCompilerError;
        }
    }
}
