using System;
using System.Collections.Generic;
using System.Linq;
using LocalAutomation.Commands;
using LocalAutomation.Core;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using LocalAutomation.Extensions.Unreal.Unreal;
using Microsoft.Extensions.Logging;
using SB.SystemUtilities.Processes;
using SB.UnrealUtilities;
using Plugin = LocalAutomation.Extensions.Unreal.Targets.Plugin;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    [Operation(SortOrder = 9)]
    public class PackagePlugin : UnrealOperation<Plugin>
    {
        private List<string> _builtTargetPlatforms = new();

        /// <summary>
        /// Composes plugin packaging with Unreal command policy and output validation callbacks.
        /// </summary>
        public PackagePlugin()
        {
            UseExecutionBehavior(new CommandProcessBehavior(
                BuildCommand,
                UnrealCommandProcessPolicy.Instance,
                resolveRetryPolicy: _ => UnrealBuildRetryPolicies.Build,
                observeOutputLine: OnOutputLine,
                processEnded: OnProcessEnded));
        }

        /// <summary>
        /// Packaging a plugin always exposes the plugin platform selection options used to build the final UAT request.
        /// </summary>
        protected override System.Collections.Generic.IEnumerable<System.Type> GetDeclaredOptionSetTypes(global::LocalAutomation.Runtime.IOperationTarget target)
        {
            return base.GetDeclaredOptionSetTypes(target)
                .Concat(new[] { typeof(PluginBuildOptions) });
        }

        protected override IEnumerable<global::LocalAutomation.Runtime.ExecutionLock> GetExecutionLocks(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            /* BuildPlugin runs through UAT and shares Unreal's writable build-rule outputs with other tool-driven build
               flows, so plugin packaging participates in the same in-process lock. It also launches AutomationTool, which
               only allows one active instance per engine install. */
            foreach (global::LocalAutomation.Runtime.ExecutionLock executionLock in base.GetExecutionLocks(operationParameters))
            {
                yield return executionLock;
            }

            yield return UnrealExecutionLocks.GetAutomationToolLock(GetRequiredTargetEngineInstall(operationParameters));
            yield return UnrealExecutionLocks.GlobalBuild;
        }

        // Fail early when the selected engine cannot even advertise the requested code platforms.
        protected override string? CheckRequirementsSatisfied(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            string? engineSelectionError = GetSingleEngineSelectionValidationMessage(operationParameters);
            if (engineSelectionError != null)
            {
                return engineSelectionError;
            }

            Engine? engine = GetTargetEngineInstall(operationParameters);
            if (engine == null)
            {
                return null;
            }

            PluginBuildOptions options = operationParameters.GetOptions<PluginBuildOptions>();
            return PluginBuildPlatformValidation.CheckRequirementsSatisfied(engine,
                PluginBuildPlatformValidation.GetRequestedTargetPlatforms(options.BuildWin64, options.BuildLinux,
                    operationParameters.GetOptions<AdditionalArgumentsOptions>().Arguments));
        }

        /// <summary>Binds packaging inputs and resets the platform observations used by process-result validation.</summary>
        private Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            // Package the plugin into a distributable output folder through UAT's BuildPlugin flow.
            PluginBuildOptions pluginBuildOptions = operationParameters.GetOptions<PluginBuildOptions>();
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            Arguments buildPluginArguments = UATArguments.CreateBuildPluginArguments(GetRequiredTarget(operationParameters).Model,
                engine, GetOutputPath(operationParameters),
                PluginBuildPlatformValidation.GetSelectedTargetPlatforms(pluginBuildOptions.BuildWin64, pluginBuildOptions.BuildLinux),
                pluginBuildOptions.StrictIncludes);
            _builtTargetPlatforms = new List<string>();
            return new Command(engine.GetRunUATPath(), buildPluginArguments.ToString());
        }

        // Track Unreal's reported target platform list as it streams by so we do not need to retain the full log.
        private void OnOutputLine(string line)
        {
            if (PluginBuildPlatformValidation.TryParseBuiltTargetPlatforms(line, out List<string> platforms))
            {
                _builtTargetPlatforms = platforms;
            }
        }

        // Compare reported platforms with the executed request, including overrides, so silent skips become failures.
        private void OnProcessEnded(global::LocalAutomation.Runtime.ExecutionTaskContext context, global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters, Command command, global::LocalAutomation.Runtime.OperationResult result)
        {
            Arguments arguments = new();
            arguments.AddRawArgsString(command.Arguments);
            List<string> requestedTargetPlatforms = PluginBuildPlatformValidation.GetRequestedTargetPlatforms(arguments);
            using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("PackagePlugin.OnProcessEnded")
                .SetTag("result.outcome", result.Outcome.ToString())
                .SetTag("result.was_cancelled", result.WasCancelled)
                .SetTag("requested_platform.count", requestedTargetPlatforms.Count);

            if (result.Outcome != global::LocalAutomation.Runtime.ExecutionTaskOutcome.Completed || result.WasCancelled || requestedTargetPlatforms.Count == 0)
            {
                activity.SetTag("validation.skipped", true);
                return;
            }

            List<string> skippedPlatforms = PluginBuildPlatformValidation.GetSkippedTargetPlatforms(requestedTargetPlatforms, _builtTargetPlatforms);
            activity.SetTag("built_platform.count", _builtTargetPlatforms.Count)
                .SetTag("skipped_platform.count", skippedPlatforms.Count);

            if (skippedPlatforms.Count == 0)
            {
                return;
            }

            context.Logger.LogError(
                "Unreal BuildPlugin skipped requested target platform(s): {Platforms}. Requested: {Requested}. Built: {Built}.",
                string.Join(", ", skippedPlatforms),
                string.Join(", ", requestedTargetPlatforms),
                _builtTargetPlatforms.Count > 0 ? string.Join(", ", _builtTargetPlatforms) : "none");
            result.Outcome = global::LocalAutomation.Runtime.ExecutionTaskOutcome.Failed;
        }

        protected override string GetOperationName()
        {
            return "Package Plugin";
        }

    }
}
