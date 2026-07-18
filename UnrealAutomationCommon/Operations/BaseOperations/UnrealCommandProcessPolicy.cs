using LocalAutomation.Commands;
using Microsoft.Extensions.Logging;

namespace UnrealAutomationCommon.Operations.BaseOperations;

/// <summary>
/// Supplies Unreal argument replacement and output classification to generic command behavior.
/// </summary>
internal static class UnrealCommandProcessPolicy
{
    // Every Unreal command behavior shares one immutable policy value.
    internal static CommandProcessPolicy Instance { get; } = new(ApplyAdditionalArguments, ClassifyOutput);

    /// <summary>
    /// Applies raw arguments to an Unreal argument collection with key replacement semantics.
    /// </summary>
    internal static void ApplyAdditionalArguments(Arguments arguments, string additionalArguments)
    {
        if (!string.IsNullOrWhiteSpace(additionalArguments))
        {
            arguments.AddRawArgsString(additionalArguments);
        }
    }

    /// <summary>
    /// Rebuilds a command through the Unreal argument parser so later keys replace generated values.
    /// </summary>
    private static void ApplyAdditionalArguments(Command command, string additionalArguments)
    {
        Arguments arguments = new();
        arguments.AddRawArgsString(command.Arguments);
        ApplyAdditionalArguments(arguments, additionalArguments);
        command.Arguments = arguments.ToString();
    }

    /// <summary>
    /// Classifies Unreal log categories, UBT diagnostics, and compiler diagnostics.
    /// </summary>
    private static LogLevel ClassifyOutput(string line)
    {
        string[] split = line.Split(new[] { ": " }, System.StringSplitOptions.None);
        LogLevel level = LogLevel.Information;
        if (split.Length > 1)
        {
            if (split[0] == "ERROR" || split[1] == "Error")
            {
                level = LogLevel.Error;
            }
            else if (split[1] == "Warning")
            {
                level = LogLevel.Warning;
            }
        }

        if (line.Contains("): error") || line.Contains(" : error ") || line.Contains("): fatal error") || line.Contains(" : fatal error"))
        {
            return LogLevel.Error;
        }

        if (line.Contains("): warning") || line.Contains(" : warning "))
        {
            return LogLevel.Warning;
        }

        return level;
    }
}
