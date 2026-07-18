using System;
using System.Linq;
using LocalAutomation.Runtime;

namespace UnrealAutomationCommon.Operations.BaseOperations
{
    /// <summary>
    /// Provides shared text mechanics for retry policies without defining failure eligibility.
    /// </summary>
    internal static class RetryFailureText
    {
        internal static string Build(ExecutionRetryContext context)
        {
            string logText = string.Join(Environment.NewLine, context.Logs.Select(entry => entry.Message));
            return context.Exception == null
                ? logText
                : logText + Environment.NewLine + context.Exception;
        }

        internal static bool Contains(string text, string value)
        {
            return text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
