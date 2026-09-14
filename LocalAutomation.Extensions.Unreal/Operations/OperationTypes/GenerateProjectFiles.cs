using LocalAutomation.Commands;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using SB.SystemUtilities.Processes;
using SB.UnrealUtilities;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
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

        /// <summary>Builds UBT project generation arguments from the selected runtime target's descriptor path.</summary>
        private Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Project project = GetRequiredTarget(operationParameters);
            Arguments args = UbtArguments.CreateProjectFilesArguments(project.Model);
            return new Command(GetRequiredTargetEngineInstall(operationParameters).GetUBTExe(), args.ToString());
        }
    }
}
