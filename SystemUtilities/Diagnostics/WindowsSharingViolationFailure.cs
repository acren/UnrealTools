using System;

namespace SystemUtilities.Diagnostics
{
    /// <summary>
    /// Matches the Windows sharing-violation diagnostic shared by workspace and UBT file-lock failures.
    /// </summary>
    public static class WindowsSharingViolationFailure
    {
        /// <summary>
        /// Requires both diagnostic fragments so unrelated file-access failures are not classified as sharing violations.
        /// </summary>
        public static bool Matches(string failureText)
        {
            return failureText.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
                && failureText.Contains("being used by another process", StringComparison.OrdinalIgnoreCase);
        }
    }
}
