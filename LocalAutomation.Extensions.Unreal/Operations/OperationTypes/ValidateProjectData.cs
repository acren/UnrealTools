using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Unreal;
using UnrealAutomationCommon;
using UnrealAutomationCommon.Unreal;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    /// <summary>
    /// Executes Unreal's native data-validation commandlet for one project without changing the selected engine's asset scope.
    /// </summary>
    internal sealed class ValidateProjectData : UnrealProcessOperation<Project>
    {
        /// <summary>
        /// Builds the installed editor commandlet invocation and leaves result handling to the inherited process operation.
        /// </summary>
        protected override global::LocalAutomation.Commands.Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Arguments arguments = UnrealArguments.MakeArguments(operationParameters, GetOutputPath(operationParameters), true);
            arguments.SetKeyValue("run", "DataValidation");
            arguments.SetFlag("unattended");
            arguments.SetFlag("nop4");

            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            return new global::LocalAutomation.Commands.Command(engine.GetEditorCmdExe(BuildConfiguration.Development), arguments.ToString());
        }
    }
}
