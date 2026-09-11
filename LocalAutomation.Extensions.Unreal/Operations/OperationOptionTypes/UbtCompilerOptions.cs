using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalAutomation.Runtime;
using UnrealUtilities;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes
{
    [PersistedSettings("ubtCompiler")]
    public partial class UbtCompilerOptions : OperationOptions
    {
        public override int SortIndex => 30;

        public override string Name => "Compiler";

        // Store the direct UBT overrides separately from general build options so unsupported UAT-based
        // operations do not accidentally advertise settings they cannot honor.
        [ObservableProperty]
        [property: DisplayName("Compiler")]
        [property: Description("Overrides the compiler used by direct UnrealBuildTool build flows when supported.")]
        private UbtCompiler compiler = UbtCompiler.Default;

        // Keep the direct UBT language-standard override on the same option set as compiler because both
        // settings apply to the exact same Build.bat-only execution path.
        [ObservableProperty]
        [property: DisplayName("C++ Standard")]
        [property: Description("Overrides the C++ language standard used by direct UnrealBuildTool build flows when supported.")]
        private UbtCppStandard cppStandard = UbtCppStandard.Default;
    }
}
