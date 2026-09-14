using System.Linq;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using SB.UnrealUtilities;
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
        protected override global::SB.SystemUtilities.Processes.Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            BuildConfiguration configuration = operationParameters.GetOptions<OperationOptionTypes.BuildConfigurationOptions>().Configuration;
            return UnrealArguments.CreateStandaloneCommand(engine, configuration, CreateLaunchRequest(operationParameters, includeProjectPath: true));
        }
    }
}
