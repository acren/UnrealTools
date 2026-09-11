using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using UnrealUtilities;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    [Operation(SortOrder = 1)]
    public class BuildEditorTarget : BuildBatOperation<Project>
    {
        // Build the project's editor target directly through Build.bat so direct UBT overrides are honored.
        protected override Arguments CreateBuildArguments(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Project project = GetRequiredTarget(operationParameters);
            return UbtArguments.CreateEditorTargetArguments(project.Model,
                operationParameters.GetOptions<BuildConfigurationOptions>().Configuration);
        }
    }
}
