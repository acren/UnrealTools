using LocalAutomation.Commands;
using LocalAutomation.Extensions.Abstractions;
using UnrealAutomationCommon.Operations.BaseOperations;
using UnrealAutomationCommon.Unreal;

namespace UnrealAutomationCommon.Operations.OperationTypes
{
    [Operation(SortOrder = 0)]
    public class GenerateProjectFiles : UnrealOperation<Project>
    {
        /// <summary>
        /// Composes project-file generation with the shared Unreal command policy.
        /// </summary>
        public GenerateProjectFiles()
        {
            UseExecutionBehavior(new CommandProcessBehavior(BuildCommand, UnrealCommandProcessPolicy.Instance));
        }

        private Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Project project = GetRequiredTarget(operationParameters);
            Arguments args = new();
            args.SetFlag("projectfiles");
            args.SetKeyPath("project", project.UProjectPath);
            args.SetFlag("game");
            args.SetFlag("rocket");
            args.SetFlag("progress");
            return new Command(GetRequiredTargetEngineInstall(operationParameters).GetUBTExe(), args.ToString());
        }
    }
}
