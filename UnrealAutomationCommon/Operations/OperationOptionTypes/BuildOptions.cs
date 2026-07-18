using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalAutomation.Runtime;

namespace UnrealAutomationCommon.Operations.OperationOptionTypes
{
    /// <summary>
    /// Provides command-line behaviors shared by Unreal compilation operations.
    /// </summary>
    [PersistedSettings("build")]
    public partial class BuildOptions : OperationOptions
    {
        public override int SortIndex => 25;

        [ObservableProperty]
        [property: DisplayName("No Hot Reload")]
        [property: Description("Disables UnrealBuildTool hot reload while compiling targets.")]
        private bool noHotReload = false;
    }
}
