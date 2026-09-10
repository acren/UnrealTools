using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalAutomation.Core.IO;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using Microsoft.Extensions.Logging;
using UnrealAutomationCommon;
using UnrealAutomationCommon.Unreal;
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
            Engine engine = project.EngineInstance ?? throw new InvalidOperationException("Clean Project requires a resolved engine install.");

            // Clean targets
            string cleanPath = Path.Combine(engine.GetBuildFolder(), "BatchFiles", "Clean.bat");
            string editorTargetName = project.Model.ProjectDescriptor?.EditorTargetName
                ?? throw new InvalidOperationException("Clean Project requires a loaded project descriptor.");
            List<string> targets = new List<string>() { project.Name, editorTargetName };
            foreach (string target in targets)
            {
                foreach (BuildConfiguration configuration in EnumUtils.GetAll<BuildConfiguration>())
                {
                    string cleanParams = $"{target} Win64 {configuration} -project=\"{project.Model.UProjectPath}\" -WaitMutex";
                    RunProcess.RunAndWait(cleanPath, cleanParams);
                }
            }

            context.Logger.LogInformation("Deleting intermediate folders");

            // Delete intermediate folders
            foreach (string path in Directory.GetDirectories(project.Model.ProjectPath, "Intermediate", SearchOption.AllDirectories))
            {
                FileUtils.DeleteDirectory(path);
            }

            context.Logger.LogInformation("Cleaning complete");

            return Task.FromResult(new global::LocalAutomation.Runtime.OperationResult(true));
        }
    }
}
