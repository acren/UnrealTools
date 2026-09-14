using System.Linq;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using SB.UnrealUtilities;
using EngineTarget = LocalAutomation.Extensions.Unreal.Targets.Engine;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    public abstract class LaunchEditor<T> : UnrealProcessOperation<T> where T : global::LocalAutomation.Runtime.OperationTarget, IEngineInstanceProvider
    {
        /// <summary>
        /// Editor launches use the selected build configuration to resolve the correct editor binary path.
        /// </summary>
        protected override System.Collections.Generic.IEnumerable<System.Type> GetDeclaredOptionSetTypes(global::LocalAutomation.Runtime.IOperationTarget target)
        {
            return base.GetDeclaredOptionSetTypes(target)
                .Concat(new[] { typeof(OperationOptionTypes.BuildConfigurationOptions) });
        }

        /// <summary>Resolves the selected editor configuration before constructing the launch command.</summary>
        protected override global::SB.SystemUtilities.Processes.Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            BuildConfiguration configuration = operationParameters.GetOptions<OperationOptionTypes.BuildConfigurationOptions>().Configuration;
            return UnrealArguments.CreateEditorCommand(engine, configuration, CreateLaunchRequest(operationParameters, includeProjectPath: true));
        }
    }

    [Operation(SortOrder = 4)]
    public class LaunchProjectEditor : LaunchEditor<Project> { }

    [Operation(SortOrder = 3)]
    public class LaunchEditor : LaunchEditor<EngineTarget> { }
}
