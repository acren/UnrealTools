using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LocalAutomation.Core;
using LocalAutomation.Runtime;

namespace LocalAutomation.Commands;

/// <summary>
/// Composes command option discovery, preview generation, process execution, and lifecycle callbacks for one operation.
/// </summary>
public sealed class CommandProcessBehavior : IOperationExecutionBehavior
{
    private readonly Func<ValidatedOperationParameters, Command> _buildCommand;
    private readonly CommandProcessPolicy _policy;
    private readonly Func<ValidatedOperationParameters, ExecutionRetryPolicy?>? _resolveRetryPolicy;
    private readonly Action<string>? _observeOutputLine;
    private readonly Action<ExecutionTaskContext, ValidatedOperationParameters, OperationResult>? _processEnded;

    /// <summary>
    /// Creates command behavior from the command builder and the task-local policy hooks the operation requires.
    /// </summary>
    public CommandProcessBehavior(
        Func<ValidatedOperationParameters, Command> buildCommand,
        CommandProcessPolicy? policy = null,
        Func<ValidatedOperationParameters, ExecutionRetryPolicy?>? resolveRetryPolicy = null,
        Action<string>? observeOutputLine = null,
        Action<ExecutionTaskContext, ValidatedOperationParameters, OperationResult>? processEnded = null)
    {
        _buildCommand = buildCommand ?? throw new ArgumentNullException(nameof(buildCommand));
        _policy = policy ?? CommandProcessPolicy.Default;
        _resolveRetryPolicy = resolveRetryPolicy;
        _observeOutputLine = observeOutputLine;
        _processEnded = processEnded;
    }

    /// <summary>
    /// Declares caller-supplied command arguments for every operation that composes this behavior.
    /// </summary>
    public IEnumerable<Type> GetRequiredOptionSetTypes(IOperationTarget target)
    {
        return new[] { typeof(AdditionalArgumentsOptions) };
    }

    /// <summary>
    /// Formats previews from the same effective-command path used by execution.
    /// </summary>
    public IReadOnlyList<string> GetPreviewTexts(ValidatedOperationParameters operationParameters)
    {
        return new[] { BuildEffectiveCommand(operationParameters).ToString() };
    }

    /// <summary>
    /// Authors one process-backed task and attaches the operation's retry policy to the complete task body.
    /// </summary>
    public void DescribeExecutionPlan(ValidatedOperationParameters operationParameters, ExecutionTaskBuilder root)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        ExecutionRetryPolicy? retryPolicy = _resolveRetryPolicy?.Invoke(operationParameters);
        if (retryPolicy != null)
        {
            root.WithRetry(retryPolicy);
        }

        root.Run(ExecuteAsync);
    }

    /// <summary>
    /// Runs one finalized command and reports the terminal result to the operation callback.
    /// </summary>
    public async Task<OperationResult> ExecuteAsync(ExecutionTaskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        ValidatedOperationParameters operationParameters = context.ValidatedOperationParameters;
        Command command = BuildEffectiveCommand(operationParameters);
        OperationResult result = await CommandProcessExecutor.ExecuteAsync(context, command, _policy, _observeOutputLine).ConfigureAwait(false);
        _processEnded?.Invoke(context, operationParameters, result);
        return result;
    }

    /// <summary>
    /// Builds the generated command and applies caller arguments exactly once for preview or execution.
    /// </summary>
    private Command BuildEffectiveCommand(ValidatedOperationParameters operationParameters)
    {
        if (operationParameters == null)
        {
            throw new ArgumentNullException(nameof(operationParameters));
        }

        using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("CommandProcessBehavior.BuildCommand");
        Command command = _buildCommand(operationParameters)
            ?? throw new InvalidOperationException("Command builder returned no command.");
        _policy.ApplyAdditionalArguments(command, operationParameters.GetOptions<AdditionalArgumentsOptions>().Arguments);
        activity.SetTag("command.file", command.File)
            .SetTag("command.has_arguments", !string.IsNullOrWhiteSpace(command.Arguments));
        return command;
    }
}
