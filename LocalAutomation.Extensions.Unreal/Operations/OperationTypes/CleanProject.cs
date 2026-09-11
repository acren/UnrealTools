using System.Threading.Tasks;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using Microsoft.Extensions.Logging;
using SystemUtilities.Processes;
using UnrealUtilities;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    public class CleanProject : UnrealOperation<Project>
    {
        protected override void DescribeExecutionPlan(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters, global::LocalAutomation.Runtime.ExecutionTaskBuilder root)
        {
            root.Run(RunCleanProjectAsync);
        }

        /// <summary>Cleans model-derived build targets and intermediate directories for the selected runtime project.</summary>
        private Task<global::LocalAutomation.Runtime.OperationResult> RunCleanProjectAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context)
        {
            context.Logger.LogInformation("Cleaning binaries");

            global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters = context.ValidatedOperationParameters;
            Project project = GetRequiredTarget(operationParameters);
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);

            // The Common recipe owns target/configuration ordering; this adapter executes each resulting command.
            foreach (Command command in UbtArguments.CreateCleanCommands(project.Model, engine))
            {
                RunProcess.RunAndWait(command.File, command.Arguments);
            }

            context.Logger.LogInformation("Deleting intermediate folders");

            project.Model.CleanIntermediateDirectories();

            context.Logger.LogInformation("Cleaning complete");

            return Task.FromResult(new global::LocalAutomation.Runtime.OperationResult(true));
        }
    }
}
