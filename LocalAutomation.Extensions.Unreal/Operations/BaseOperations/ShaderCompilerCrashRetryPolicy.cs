using System;
using LocalAutomation.Runtime;
using UnrealUtilities;

namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations
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

        /// <summary>Adapts attempt diagnostics to the independent shader crash classifier.</summary>
        private static bool Matches(ExecutionRetryContext context)
        {
            return UnrealFailureClassifier.IsShaderCompilerCrash(RetryFailureText.Build(context));
        }
    }
}
