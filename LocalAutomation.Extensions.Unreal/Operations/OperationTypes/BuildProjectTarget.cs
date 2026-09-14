using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using SB.UnrealUtilities;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    /// <summary>
    /// Builds the project's game target directly through Build.bat so package-only BuildCookRun passes can stage against
    /// an existing game receipt instead of holding the shared Unreal build lock through cook and package phases.
    /// </summary>
    internal sealed class BuildProjectTarget : BuildBatOperation<Project>
    {
        /// <summary>
        /// Uses the project's primary target name so direct UBT compilation produces the game receipt that staging later
        /// expects to find in Binaries/Win64.
        /// </summary>
        protected override Arguments CreateBuildArguments(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Project project = GetRequiredTarget(operationParameters);
            ProjectTargetBuildSpec buildTarget = ProjectTargetBuildSpec.ForGameTarget(project.Model, operationParameters.GetOptions<OperationOptionTypes.BuildConfigurationOptions>().Configuration);

            return UbtArguments.CreateProjectTargetArguments(project.Model, buildTarget);
        }

        /// <summary>
        /// Keeps deploy logs explicit about the direct game-target compile step that precedes package-only BuildCookRun.
        /// </summary>
        protected override string GetOperationName()
        {
            return "Build Project Target";
        }
    }
}
