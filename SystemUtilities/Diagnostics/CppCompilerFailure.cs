using System;

namespace SystemUtilities.Diagnostics;

/// <summary>
/// Recognizes native compiler frontend crashes and internal compiler errors from vendor diagnostics.
/// </summary>
public static class CppCompilerFailure
{
    /// <summary>
    /// Requires supported compiler signatures; generic exit codes and ordinary source diagnostics do not match.
    /// </summary>
    public static bool Matches(string failureText)
    {
        // Both Clang fragments distinguish a frontend crash from unrelated LLVM bug-report guidance.
        bool clangFrontendCrash =
            failureText.Contains("PLEASE submit a bug report to https://github.com/llvm/llvm-project/issues/", StringComparison.OrdinalIgnoreCase)
            && failureText.Contains("clang frontend command failed due to signal", StringComparison.OrdinalIgnoreCase);
        bool msvcInternalCompilerError =
            failureText.Contains("fatal error C1001: Internal compiler error", StringComparison.OrdinalIgnoreCase);
        return clangFrontendCrash || msvcInternalCompilerError;
    }
}
