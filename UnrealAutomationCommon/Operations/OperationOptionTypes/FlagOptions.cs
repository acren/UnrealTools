using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalAutomation.Runtime;

namespace UnrealAutomationCommon.Operations.OperationOptionTypes
{
    [PersistedSettings("flag")]
    public partial class FlagOptions : OperationOptions
    {
        public override int SortIndex => 40;

        public override string Name => "Flags";

        [ObservableProperty]
        [property: DisplayName("StompMalloc")]
        [property: Description("Enables Unreal's stomp allocator diagnostics to help catch memory corruption issues.")]
        private bool stompMalloc = false;

        [ObservableProperty]
        [property: DisplayName("Wait For Attach")]
        [property: Description("Pauses startup long enough for a debugger to attach before the process continues.")]
        private bool waitForAttach = false;

        // NoMessaging is an explicit Unreal process switch, so it lives with the other launch flags.
        [ObservableProperty]
        [property: DisplayName("No Messaging")]
        [property: Description("Adds -NoMessaging to launched Unreal processes to disable Unreal's message bus transports.")]
        private bool noMessaging = false;

        // DDC force-memory mode gives validation launches a writable transient cache when shared Zen storage is unavailable.
        [ObservableProperty]
        [property: DisplayName("DDC Force Memory Cache")]
        [property: Description("Adds -DDC-ForceMemoryCache to launched Unreal processes so Derived Data Cache writes stay process-local.")]
        private bool ddcForceMemoryCache = false;
    }
}
