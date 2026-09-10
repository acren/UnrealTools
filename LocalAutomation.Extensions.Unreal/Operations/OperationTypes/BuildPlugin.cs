using System;
using LocalAutomation.Core;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using UnrealAutomationCommon;
using UnrealAutomationCommon.Unreal;
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
            if (plugin.Model.IsBlueprintOnly)
            {
                activity.SetTag("result", "Build Plugin only supports code plugins");
                return "Build Plugin only supports code plugins";
            }

            Project hostProject = plugin.GetHostProjectForDiagnostics();
            activity.SetTag("host_project.path", hostProject.Model.ProjectPath);
            if (!hostProject.IsValid)
            {
                activity.SetTag("result", "Build Plugin requires the plugin to live inside a valid host project");
                return "Build Plugin requires the plugin to live inside a valid host project";
            }

            Engine? engine = hostProject.GetEngineInstanceForDiagnostics();
            activity.SetTag("engine.name", engine?.DisplayName ?? string.Empty);
            if (engine == null)
            {
                activity.SetTag("result", "Build Plugin could not resolve a host project engine install");
                return "Build Plugin could not resolve a host project engine install";
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
        protected override void ConfigureBuildArguments(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters, Arguments args)
        {
            Plugin plugin = GetRequiredTarget(operationParameters);
            Project hostProject = plugin.GetHostProjectForDiagnostics();
            if (!hostProject.IsValid)
            {
                throw new InvalidOperationException("Build Plugin requires a valid host project before command generation.");
            }

            // Match Unreal's direct plugin build flow: editor target, platform, configuration, host project, then plugin path.
            args.SetArgument(GetRequiredTargetEngineInstall(operationParameters).BaseEditorName);
            args.SetArgument("Win64");
            args.SetArgument(operationParameters.GetOptions<BuildConfigurationOptions>().Configuration.ToString());
            args.SetPath(hostProject.Model.UProjectPath);
            args.SetKeyPath("plugin", plugin.Model.UPluginPath);
        }
    }
}
