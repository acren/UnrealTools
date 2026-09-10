using System.Linq;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Unreal;
using UnrealAutomationCommon;
using UnrealAutomationCommon.Unreal;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    [Operation(SortOrder = 5)]
    public class LaunchStandalone : UnrealProcessOperation<Project>
    {
        /// <summary>
        /// Standalone launches reuse the selected editor build configuration when resolving the game executable path.
        /// </summary>
        protected override System.Collections.Generic.IEnumerable<System.Type> GetDeclaredOptionSetTypes(global::LocalAutomation.Runtime.IOperationTarget target)
        {
            return base.GetDeclaredOptionSetTypes(target)
                .Concat(new[] { typeof(OperationOptionTypes.BuildConfigurationOptions) });
        }

        /// <summary>Launches the selected editor binary in standalone-game mode using the requested configuration.</summary>
        protected override global::LocalAutomation.Commands.Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Arguments args = UnrealArguments.MakeArguments(operationParameters, GetOutputPath(operationParameters), true);
            args.SetFlag("game");
            args.SetFlag("windowed");
            args.SetKeyValue("resx", "1920", false);
            args.SetKeyValue("resy", "1080", false);
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            BuildConfiguration configuration = operationParameters.GetOptions<OperationOptionTypes.BuildConfigurationOptions>().Configuration;
            return new global::LocalAutomation.Commands.Command(engine.GetEditorExe(configuration), args.ToString());
        }
    }
}
