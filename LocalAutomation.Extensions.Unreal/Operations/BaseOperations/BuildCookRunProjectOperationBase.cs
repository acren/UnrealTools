using System;
using System.Collections.Generic;
using System.Linq;
using LocalAutomation.Commands;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using LocalAutomation.Extensions.Unreal.Unreal;
using LocalAutomation.Runtime;
using SB.SystemUtilities.Processes;
using SB.UnrealUtilities;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations
{
    /// <summary>
    /// Centralizes project-oriented BuildCookRun command assembly so concrete operations can differ only in which phases
    /// they run and which option groups they expose.
    /// </summary>
    public abstract class BuildCookRunProjectOperationBase : UnrealOperation<Project>
    {
        /// <summary>
        /// Composes BuildCookRun process execution with Unreal command policy and retry behavior.
        /// </summary>
        protected BuildCookRunProjectOperationBase()
        {
            UseExecutionBehavior(new CommandProcessBehavior(BuildCommand, UnrealCommandProcessPolicy.Instance, GetExecutionRetryPolicy));
        }

        /// <summary>
        /// BuildCookRun operations expose build behavior for requests that include compilation.
        /// </summary>
        protected override IEnumerable<Type> GetDeclaredOptionSetTypes(IOperationTarget target)
        {
            return base.GetDeclaredOptionSetTypes(target)
                .Concat(new[] { typeof(BuildOptions) });
        }

        /// <summary>
        /// Returns the phase request that defines one concrete BuildCookRun invocation for the current parameter state.
        /// </summary>
        protected abstract BuildCookRunProjectRequest GetBuildCookRunRequest(ValidatedOperationParameters operationParameters);

        /// <summary>
        /// BuildCookRun only needs the shared Unreal build lock when the selected phase set still compiles binaries.
        /// </summary>
        protected override IEnumerable<ExecutionLock> GetExecutionLocks(ValidatedOperationParameters operationParameters)
        {
            foreach (ExecutionLock executionLock in base.GetExecutionLocks(operationParameters))
            {
                yield return executionLock;
            }

            /* Every BuildCookRun invocation launches RunUAT, and AutomationTool itself only allows one active instance per
               engine install. Serialize those calls per resolved engine so package-only flows can still run in parallel
               across different engine installs. */
            yield return UnrealExecutionLocks.GetAutomationToolLock(GetRequiredTargetEngineInstall(operationParameters));

            if (GetBuildCookRunRequest(operationParameters).HasPhase(BuildCookRunProjectPhases.Build))
            {
                yield return UnrealExecutionLocks.GlobalBuild;
            }
        }

        /// <summary>
        /// Selects retryable failure classes from the build and cook phases enabled on this aggregate UAT task.
        /// </summary>
        private ExecutionRetryPolicy? GetExecutionRetryPolicy(ValidatedOperationParameters operationParameters)
        {
            BuildCookRunProjectRequest request = GetBuildCookRunRequest(operationParameters);
            bool builds = request.HasPhase(BuildCookRunProjectPhases.Build);
            bool cooks = request.HasPhase(BuildCookRunProjectPhases.Cook);

            // Compiler crashes belong only to the phase that launches that compiler.
            return (builds, cooks) switch
            {
                (true, true) => UnrealBuildRetryPolicies.BuildAndCook,
                (true, false) => UnrealBuildRetryPolicies.Build,
                (false, true) => UnrealBuildRetryPolicies.Cook,
                _ => BuildToolConflictRetryPolicy.Instance
            };
        }

        /// <summary>
        /// Builds one BuildCookRun command from the shared request model so concrete operations only need to describe the
        /// enabled phases and explicit command settings, not the UAT argument plumbing.
        /// </summary>
        private Command BuildCommand(ValidatedOperationParameters operationParameters)
        {
            Engine engine = GetRequiredTargetEngineInstall(operationParameters);
            Project project = GetRequiredTarget(operationParameters);
            BuildCookRunProjectRequest request = GetBuildCookRunRequest(operationParameters);
            Arguments arguments = UATArguments.CreateBuildCookRunArguments(project.Model, engine, request,
                request.HasPhase(BuildCookRunProjectPhases.Build) && operationParameters.GetOptions<BuildOptions>().NoHotReload);
            return new Command(engine.GetRunUATPath(), arguments.ToString());
        }
    }

    /// <summary>
    /// Lets higher-level workflows such as Deploy Plugin compose custom BuildCookRun invocations without expanding the
    /// public operation catalog for every transient phase preset.
    /// </summary>
    internal sealed class ConfiguredBuildCookRunProjectOperation : BuildCookRunProjectOperationBase
    {
        private readonly string _operationName;
        private readonly BuildCookRunProjectRequest _request;

        public ConfiguredBuildCookRunProjectOperation(string operationName, BuildCookRunProjectRequest request)
        {
            _operationName = string.IsNullOrWhiteSpace(operationName)
                ? throw new ArgumentException("Operation name is required.", nameof(operationName))
                : operationName;
            _request = request;
        }

        /// <summary>
        /// Returns the preconfigured BuildCookRun request captured when this transient operation instance was created.
        /// </summary>
        protected override BuildCookRunProjectRequest GetBuildCookRunRequest(ValidatedOperationParameters operationParameters)
        {
            return _request;
        }

        /// <summary>
        /// Preserves the caller-provided display name so logs and child-operation failures describe the specific phase
        /// preset Deploy Plugin asked for.
        /// </summary>
        protected override string GetOperationName()
        {
            return _operationName;
        }
    }
}
