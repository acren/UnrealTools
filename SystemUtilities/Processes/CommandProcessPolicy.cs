using System;
using Microsoft.Extensions.Logging;
using SystemUtilities.Formatting;

namespace SystemUtilities.Processes;

/// <summary>
/// Defines argument merging and stdout classification for one command toolchain.
/// </summary>
public sealed class CommandProcessPolicy
{
    // The generic policy appends opaque arguments and treats stdout as informational output.
    public static CommandProcessPolicy Default { get; } = new(AppendAdditionalArguments, _ => LogLevel.Information);

    private readonly Action<Command, string> _mergeAdditionalArguments;
    private readonly Func<string, LogLevel> _classifyOutput;

    /// <summary>
    /// Creates a policy from deterministic argument-merging and output-classification functions.
    /// </summary>
    public CommandProcessPolicy(Action<Command, string> mergeAdditionalArguments, Func<string, LogLevel> classifyOutput)
    {
        _mergeAdditionalArguments = mergeAdditionalArguments ?? throw new ArgumentNullException(nameof(mergeAdditionalArguments));
        _classifyOutput = classifyOutput ?? throw new ArgumentNullException(nameof(classifyOutput));
    }

    /// <summary>
    /// Applies caller-supplied arguments to a generated command when any are present.
    /// </summary>
    public void ApplyAdditionalArguments(Command command, string additionalArguments)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (!string.IsNullOrWhiteSpace(additionalArguments))
        {
            _mergeAdditionalArguments(command, additionalArguments);
        }
    }

    /// <summary>
    /// Returns the log level for one non-empty stdout line.
    /// </summary>
    public LogLevel ClassifyOutput(string line)
    {
        return _classifyOutput(line ?? throw new ArgumentNullException(nameof(line)));
    }

    /// <summary>
    /// Appends opaque arguments without interpreting tool-specific keys.
    /// </summary>
    private static void AppendAdditionalArguments(Command command, string additionalArguments)
    {
        string arguments = command.Arguments;
        CommandLineFormatting.CombineArgs(ref arguments, additionalArguments);
        command.Arguments = arguments;
    }
}
