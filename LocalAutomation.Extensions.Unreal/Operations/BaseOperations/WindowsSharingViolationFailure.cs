namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations
{
    /// <summary>
    /// Matches the Windows sharing-violation diagnostic shared by workspace and UBT file-lock failures.
    /// </summary>
    internal static class WindowsSharingViolationFailure
    {
        internal static bool Matches(string failureText)
        {
            return RetryFailureText.Contains(failureText, "cannot access the file")
                && RetryFailureText.Contains(failureText, "being used by another process");
        }
    }
}
