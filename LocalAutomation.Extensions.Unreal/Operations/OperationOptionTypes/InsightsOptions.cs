using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalAutomation.Extensions.Unreal.Operations.OptionChoiceSources;
using LocalAutomation.Runtime;
using UnrealUtilities;


namespace LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes
{
    [PersistedSettings("insights")]
    public partial class InsightsOptions : OperationOptions
    {
        public override int SortIndex => 30;

        [ObservableProperty]
        [property: DisplayName("Trace Channels")]
        [property: Description("Selects the Unreal Insights trace channels to enable for the launched process.")]
        [property: ChoiceCollectionSource(typeof(TraceChannelChoiceSource))]
        private IReadOnlyList<TraceChannel> traceChannels = Array.Empty<TraceChannel>();
    }
}
