using LocalAutomation.Core;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using UnrealUtilities;
using Plugin = LocalAutomation.Extensions.Unreal.Targets.Plugin;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    [Operation(SortOrder = 8)]
    public class BuildPlugin : BuildBatOperation<Plugin>
    {
        // Direct Build.bat plugin compilation needs a host project and only applies to code plugins.
        protected override string? CheckRequirementsSatisfied(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("BuildPlugin.CheckRequirements");
            activity.SetTag("target.type", operationParameters.Target?.GetType().Name ?? string.Empty);
            string? engineSelectionError = GetSingleEngineSelectionValidationMessage(operationParameters);
            if (engineSelectionError != null)
            {
                activity.SetTag("result", engineSelectionError);
                return engineSelectionError;
            }

            Plugin plugin = GetRequiredTarget(operationParameters);
            activity.SetTag("plugin.path", plugin.Model.PluginPath)
                .SetTag("descriptor.path", plugin.Model.UPluginPath);
            // Resolve host inputs only after the code-plugin prerequisite, keeping invalid inputs out of engine lookup.
            Project? hostProject = plugin.Model.IsBlueprintOnly ? null : plugin.GetHostProjectForDiagnostics();
            Engine? engine = null;
            if (hostProject != null)
            {
                activity.SetTag("host_project.path", hostProject.Model.ProjectPath);
                if (hostProject.IsValid)
                {
                    // Validation and command construction share explicit operation engine selection.
                    engine = GetTargetEngineInstall(operationParameters);
                    activity.SetTag("engine.name", engine?.DisplayName ?? string.Empty);
                }
            }

            string? pluginRequirementsError = UbtArguments.CheckPluginBuildRequirements(plugin.Model, hostProject?.Model, engine);
            if (pluginRequirementsError != null)
            {
                activity.SetTag("result", pluginRequirementsError);
                return pluginRequirementsError;
            }

            string? buildBatRequirementsError = base.CheckRequirementsSatisfied(operationParameters);
            if (buildBatRequirementsError != null)
            {
                // Shared Build.bat validation covers compiler, language-standard, and engine-selection rules.
                activity.SetTag("result", buildBatRequirementsError);
                return buildBatRequirementsError;
            }

            activity.SetTag("result", "Success");
            return null;
        }

        // Build the plugin against its host project through Build.bat so plugin compilation stays in place.
        protected override Arguments CreateBuildArguments(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            Plugin plugin = GetRequiredTarget(operationParameters);
            Project hostProject = plugin.GetHostProjectForDiagnostics();
            return UbtArguments.CreatePluginBuildArguments(plugin.Model, hostProject.Model,
                GetRequiredTargetEngineInstall(operationParameters),
                operationParameters.GetOptions<BuildConfigurationOptions>().Configuration);
        }
    }
}
