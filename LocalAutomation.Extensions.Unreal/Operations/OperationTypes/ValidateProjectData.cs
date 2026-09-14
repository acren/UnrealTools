using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using SB.UnrealUtilities;
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
        protected override global::SB.SystemUtilities.Processes.Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            return UnrealArguments.CreateDataValidationCommand(engine, CreateLaunchRequest(operationParameters, includeProjectPath: true));
        }
    }
}
