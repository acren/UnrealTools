using System;
using System.Collections.Generic;

namespace LocalAutomation.Runtime;

/// <summary>
/// Contributes one operation's option metadata, generic preview text, and default execution plan.
/// </summary>
public interface IOperationExecutionBehavior
{
    /// <summary>
    /// Returns the option-set types required by the behavior for the provided target.
    /// </summary>
    IEnumerable<Type> GetRequiredOptionSetTypes(IOperationTarget target);

    /// <summary>
    /// Returns user-facing previews for validated operation parameters.
    /// </summary>
    IReadOnlyList<string> GetPreviewTexts(ValidatedOperationParameters operationParameters);

    /// <summary>
    /// Authors the behavior's tasks beneath the framework-owned operation root.
    /// </summary>
    void DescribeExecutionPlan(ValidatedOperationParameters operationParameters, ExecutionTaskBuilder root);
}
