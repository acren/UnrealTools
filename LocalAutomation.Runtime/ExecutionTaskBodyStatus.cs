namespace LocalAutomation.Runtime;

/// <summary>
/// Describes whether an execution task body is absent, inactive, executing its own code, or waiting for inserted child work.
/// </summary>
internal enum ExecutionTaskBodyStatus
{
    /// <summary>
    /// The task has no authored executable body; declared locks behave as subtree reservations while the scope is open.
    /// </summary>
    None,

    /// <summary>
    /// The task has an authored executable body, but that body is not currently alive.
    /// </summary>
    NotExecuting,

    /// <summary>
    /// The task body is executing its own task code and declared locks behave as active ownership.
    /// </summary>
    Executing,

    /// <summary>
    /// The task body is alive but suspended inside inserted child-operation work.
    /// </summary>
    WaitingForChild
}
