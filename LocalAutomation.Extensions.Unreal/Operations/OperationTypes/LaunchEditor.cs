using System.Linq;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Unreal;
using UnrealAutomationCommon.Unreal;
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
        protected override global::LocalAutomation.Commands.Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            BuildConfiguration configuration = operationParameters.GetOptions<OperationOptionTypes.BuildConfigurationOptions>().Configuration;
            return new global::LocalAutomation.Commands.Command(engine.GetEditorExe(configuration), UnrealArguments.MakeArguments(operationParameters, GetOutputPath(operationParameters), true).ToString());
        }
    }

    [Operation(SortOrder = 4)]
    public class LaunchProjectEditor : LaunchEditor<Project> { }

    [Operation(SortOrder = 3)]
    public class LaunchEditor : LaunchEditor<EngineTarget> { }
}
