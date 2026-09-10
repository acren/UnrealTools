using System.Collections;
using LocalAutomation.Runtime;

namespace LocalAutomation.Extensions.Unreal.Operations.OptionChoiceSources;

/// <summary>
/// Supplies the known Unreal Insights trace channels used by trace configuration option editors.
/// </summary>
public sealed class TraceChannelChoiceSource : IChoiceCollectionSource
{
    /// <summary>
    /// Returns the known trace channels that can be selected for an Insights launch.
    /// </summary>
    public IEnumerable GetChoices(object? component, string propertyName)
    {
        return UnrealAutomationCommon.Unreal.TraceChannels.Channels;
    }
}
