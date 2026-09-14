using SB.SystemUtilities.Processes;
using SB.UnrealUtilities;

namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations;

/// <summary>
/// Supplies Unreal argument replacement and output classification to generic command behavior.
/// </summary>
internal static class UnrealCommandProcessPolicy
{
    // Every Unreal command behavior shares one immutable policy value.
    internal static CommandProcessPolicy Instance { get; } = new(ApplyAdditionalArguments, UnrealLogUtils.ClassifyOutput);

    /// <summary>
    /// Rebuilds a command through the Unreal argument parser so later keys replace generated values.
    /// </summary>
    private static void ApplyAdditionalArguments(Command command, string additionalArguments)
    {
        Arguments arguments = new();
        arguments.AddRawArgsString(command.Arguments);
        if (!string.IsNullOrWhiteSpace(additionalArguments))
        {
            arguments.AddRawArgsString(additionalArguments);
        }
        command.Arguments = arguments.ToString();
    }

}
