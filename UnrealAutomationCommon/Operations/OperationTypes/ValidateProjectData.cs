using LocalAutomation.Extensions.Abstractions;
using UnrealAutomationCommon.Operations.BaseOperations;
using UnrealAutomationCommon.Operations.OperationOptionTypes;
using UnrealAutomationCommon.Unreal;

namespace UnrealAutomationCommon.Operations.OperationTypes
{
    /// <summary>
    /// Executes Unreal's native data-validation commandlet for one project without changing the selected engine's asset scope.
    /// </summary>
    internal sealed class ValidateProjectData : UnrealProcessOperation<Project>
    {
        /// <summary>
        /// Builds the installed editor commandlet invocation and leaves result handling to the inherited process operation.
        /// </summary>
        protected override global::LocalAutomation.Runtime.Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Arguments arguments = UnrealArguments.MakeArguments(operationParameters, GetOutputPath(operationParameters), true);
            arguments.SetKeyValue("run", "DataValidation");
            arguments.SetFlag("unattended");
            arguments.SetFlag("nop4");

            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            return new global::LocalAutomation.Runtime.Command(engine.GetEditorCmdExe(BuildConfiguration.Development), arguments.ToString());
        }
    }
}
