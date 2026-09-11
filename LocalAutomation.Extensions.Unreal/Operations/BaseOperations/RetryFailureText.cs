using System;
using System.Linq;
using LocalAutomation.Runtime;

namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations
{
    /// <summary>
    /// Collects attempt diagnostics for independent failure classifiers without defining retry eligibility.
    /// </summary>
    internal static class RetryFailureText
    {
        /// <summary>Combines captured output and the attempt exception in their diagnostic order.</summary>
        internal static string Build(ExecutionRetryContext context)
        {
            string logText = string.Join(Environment.NewLine, context.Logs.Select(entry => entry.Message));
            return context.Exception == null
                ? logText
                : logText + Environment.NewLine + context.Exception;
        }
    }
}
