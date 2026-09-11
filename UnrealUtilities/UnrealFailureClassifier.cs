using System;
using SystemUtilities.Diagnostics;

namespace UnrealUtilities;

/// <summary>Recognizes transient Unreal tool failures without choosing retry budgets or executing tasks.</summary>
public static class UnrealFailureClassifier
{
    /// <summary>Recognizes contention on UBT's shared rule assembly or its single running instance.</summary>
    public static bool IsBuildToolConflict(string failureText)
    {
        bool sharedBuildRulesLock = failureText.Contains("BuildRules", StringComparison.OrdinalIgnoreCase)
            && failureText.Contains("MarketplaceRules.dll", StringComparison.OrdinalIgnoreCase)
            && WindowsSharingViolationFailure.Matches(failureText);
        return sharedBuildRulesLock
            || failureText.Contains("A conflicting instance of UnrealBuildTool is already running", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Recognizes shader worker and platform compiler crash signatures.</summary>
    public static bool IsShaderCompilerCrash(string failureText)
    {
        return failureText.Contains("ShaderCompileWorker failed", StringComparison.OrdinalIgnoreCase)
            || failureText.Contains("Crash inside the platform compiler", StringComparison.OrdinalIgnoreCase);
    }
}
