using System;
using System.Linq;
using LocalAutomation.Runtime;

namespace UnrealAutomationCommon.Operations.BaseOperations
{
    /// <summary>
    /// Provides retry policies for transient Unreal build-tool failures that are safe to rerun as complete task bodies.
    /// </summary>
    internal static class UnrealBuildRetryPolicies
    {
        /// <summary>
        /// Retries UBT/UAT failures caused by transient contention on shared Unreal build-tool state.
        /// </summary>
        public static ExecutionRetryPolicy TransientBuildToolConflictPolicy { get; } = new(
            maxAttempts: 5,
            shouldRetry: IsTransientBuildToolConflictFailure,
            getDelay: context => TimeSpan.FromSeconds(1 << context.AttemptNumber));

        /// <summary>
        /// Matches known build-tool contention diagnostics so ordinary compile errors stay single-attempt.
        /// </summary>
        private static bool IsTransientBuildToolConflictFailure(ExecutionRetryContext context)
        {
            string failureText = BuildFailureText(context);
            return IsSharedBuildRuleFileLockFailure(failureText)
                || Contains(failureText, "A conflicting instance of UnrealBuildTool is already running");
        }

        /// <summary>
        /// Matches the shared BuildRules assembly cache file-lock failure emitted by launcher-engine UBT instances.
        /// </summary>
        private static bool IsSharedBuildRuleFileLockFailure(string failureText)
        {
            return Contains(failureText, "BuildRules")
                && Contains(failureText, "MarketplaceRules.dll")
                && Contains(failureText, "cannot access the file")
                && Contains(failureText, "being used by another process");
        }

        /// <summary>
        /// Combines buffered attempt logs and exception text into one classifier input.
        /// </summary>
        private static string BuildFailureText(ExecutionRetryContext context)
        {
            string logText = string.Join(Environment.NewLine, context.Logs.Select(entry => entry.Message));
            if (context.Exception == null)
            {
                return logText;
            }

            return logText + Environment.NewLine + context.Exception;
        }

        /// <summary>
        /// Performs case-insensitive substring checks for UBT diagnostic text.
        /// </summary>
        private static bool Contains(string text, string value)
        {
            return text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
