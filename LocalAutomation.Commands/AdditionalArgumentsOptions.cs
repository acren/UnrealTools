using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalAutomation.Runtime;

namespace LocalAutomation.Commands;

/// <summary>
/// Stores freeform pass-through arguments for command-backed operations.
/// </summary>
[PersistedSettings("additionalArguments")]
public sealed partial class AdditionalArgumentsOptions : OperationOptions
{
    /// <summary>
    /// Keep this option set at the end of the list because it acts as a final override layer rather than a primary
    /// configuration category.
    /// </summary>
    public override int SortIndex => 10_000;

    /// <summary>
    /// Give the option set a clearer title than the default type-name splitting.
    /// </summary>
    public override string Name => "Additional Arguments";

    /// <summary>
    /// Gets the raw argument string appended to the generated command.
    /// </summary>
    [ObservableProperty]
    [property: DisplayName("Arguments")]
    [property: Description("Appends raw command-line arguments after the generated automation command.")]
    [property: PersistedValue(PersistenceScope.UserTargetOverride, "commands.additionalArguments.arguments")]
    private string arguments = string.Empty;
}
