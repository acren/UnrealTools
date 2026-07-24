using LocalAutomation.Runtime;

namespace UnrealAutomationCommon.Operations.BaseOperations
{
    /// <summary>
    /// Composes independently owned retry policies for Unreal build and cook phases.
    /// </summary>
    internal static class UnrealBuildRetryPolicies
    {
        internal static ExecutionRetryPolicy Build { get; } =
            ExecutionRetryPolicy.Or(BuildToolConflictRetryPolicy.Instance, CppCompilerCrashRetryPolicy.Instance);

        internal static ExecutionRetryPolicy Cook { get; } =
            ExecutionRetryPolicy.Or(BuildToolConflictRetryPolicy.Instance, ShaderCompilerCrashRetryPolicy.Instance);

        internal static ExecutionRetryPolicy BuildAndCook { get; } =
            ExecutionRetryPolicy.Or(BuildToolConflictRetryPolicy.Instance, CppCompilerCrashRetryPolicy.Instance, ShaderCompilerCrashRetryPolicy.Instance);
    }
}
