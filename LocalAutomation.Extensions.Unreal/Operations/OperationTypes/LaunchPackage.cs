using System;
using System.Linq;
using System.Threading.Tasks;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using UnrealUtilities;
using IPackageProvider = LocalAutomation.Extensions.Unreal.Targets.IPackageProvider;
using Package = LocalAutomation.Extensions.Unreal.Targets.Package;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    public abstract class LaunchPackage<T> : UnrealProcessOperation<T> where T : global::LocalAutomation.Runtime.OperationTarget, IPackageProvider
    {
        /// <summary>
        /// Package launch flows expose automation settings because tests can optionally run against the launched build.
        /// </summary>
        protected override System.Collections.Generic.IEnumerable<System.Type> GetDeclaredOptionSetTypes(global::LocalAutomation.Runtime.IOperationTarget target)
        {
            return base.GetDeclaredOptionSetTypes(target)
                .Concat(new[] { typeof(OperationOptionTypes.BuildConfigurationOptions) });
        }

        /// <summary>Checks staged-package availability without retaining a temporary runtime wrapper.</summary>
        protected override string? CheckRequirementsSatisfied(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            T target = GetRequiredTarget(operationParameters);
            Engine? engine = GetTargetEngineInstall(operationParameters);
            if (engine == null)
            {
                return "Provided package is null";
            }

            Package? package = target.GetProvidedPackage(engine);
            // Project providers create caller-owned wrappers; a package target provides itself and stays selected.
            using Package? ownedPackage = ReferenceEquals(package, target) ? null : package;
            return package == null ? "Provided package is null" : null;
        }

        /// <summary>Builds a package launch command and releases any provider-created wrapper after reading its model.</summary>
        protected override global::SystemUtilities.Processes.Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            T target = GetRequiredTarget(operationParameters);
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            Package package = target.GetProvidedPackage(engine)
                ?? throw new InvalidOperationException("Launch Package requires a packaged build before command generation.");
            // The selected target owns itself; only a distinct provider-created wrapper belongs to this call.
            using Package? ownedPackage = ReferenceEquals(package, target) ? null : package;
            return UnrealArguments.CreatePackageCommand(package.Model, CreateLaunchRequest(operationParameters));
        }

        protected override void DescribeExecutionPlan(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters, global::LocalAutomation.Runtime.ExecutionTaskBuilder root)
        {
            root.Run(RunLaunchPackageAsync);
        }

        /// <summary>Prepares the engine report template for automation runs before executing the package command.</summary>
        private async Task<global::LocalAutomation.Runtime.OperationResult> RunLaunchPackageAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context)
        {
            global::LocalAutomation.Runtime.ValidatedOperationParameters validatedParameters = context.ValidatedOperationParameters;
            AutomationOptions automationOptions = validatedParameters.GetOptions<AutomationOptions>();
            if (automationOptions.RunTests)
            {
                T target = GetRequiredTarget(validatedParameters);
                Engine engine = GetRequiredTargetEngineInstall(validatedParameters);
                Package package = target.GetProvidedPackage(engine)
                    ?? throw new InvalidOperationException("Launch Package requires a packaged build before execution.");
                // Release staged-package wrappers even when report-template preparation fails.
                using Package? ownedPackage = ReferenceEquals(package, target) ? null : package;

                package.Model.PrepareAutomationReportTemplate(engine);
            }

            return await ExecuteCommandAsync(context);
        }

        protected override string GetOperationName()
        {
            return "Launch Package";
        }

        /// <summary>Returns model-derived package logs while releasing a temporary caller-owned runtime wrapper.</summary>
        public override string GetLogsPath(global::LocalAutomation.Runtime.OperationParameters operationParameters)
        {
            global::LocalAutomation.Runtime.ValidatedOperationParameters validatedParameters = ValidateParameters(operationParameters);
            T target = GetRequiredTarget(validatedParameters);
            Engine engine = GetRequiredTargetEngineInstall(validatedParameters);
            Package package = target.GetProvidedPackage(engine)
                ?? throw new InvalidOperationException("Launch Package requires a packaged build before log discovery.");
            // Keep the selected package alive while bounding a project's staged-package wrapper to this lookup.
            using Package? ownedPackage = ReferenceEquals(package, target) ? null : package;
            return package.Model.LogsPath;
        }
    }

    public class LaunchPackage : LaunchPackage<Package>
    {
    }

    [Operation(SortOrder = 7)]
    public class LaunchStagedPackage : LaunchPackage<Project>
    {
    }
}
