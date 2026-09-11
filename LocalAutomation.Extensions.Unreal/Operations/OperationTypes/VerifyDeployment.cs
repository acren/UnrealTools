using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LocalAutomation.Commands;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Extensions.Unreal.Operations.BaseOperations;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using LocalAutomation.Extensions.Unreal.Unreal;
using Microsoft.Extensions.Logging;
using UnrealUtilities;
using Package = LocalAutomation.Extensions.Unreal.Targets.Package;
using Plugin = LocalAutomation.Extensions.Unreal.Targets.Plugin;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;

namespace LocalAutomation.Extensions.Unreal.Operations.OperationTypes
{
    [Operation(SortOrder = 11)]
    public class VerifyDeployment : UnrealOperation<Plugin>
    {
        private sealed class VerificationState
        {
            public VerificationState(Plugin sourcePlugin, Engine engine, global::LocalAutomation.Runtime.Workspace preparedWorkspace, Project exampleProject, string tempPath, string packageOutputPath)
            {
                SourcePlugin = sourcePlugin ?? throw new ArgumentNullException(nameof(sourcePlugin));
                Engine = engine ?? throw new ArgumentNullException(nameof(engine));
                PreparedWorkspace = preparedWorkspace ?? throw new ArgumentNullException(nameof(preparedWorkspace));
                ExampleProject = exampleProject ?? throw new ArgumentNullException(nameof(exampleProject));
                TempPath = tempPath ?? throw new ArgumentNullException(nameof(tempPath));
                PackageOutputPath = packageOutputPath ?? throw new ArgumentNullException(nameof(packageOutputPath));
            }

            /// <summary>
            /// Gets the source plugin whose installed engine copy is being verified.
            /// </summary>
            public Plugin SourcePlugin { get; }

            /// <summary>
            /// Gets the Unreal Engine installation used for this verification branch.
            /// </summary>
            public Engine Engine { get; }

            /// <summary>
            /// Gets the persistent workspace that owns the prepared verification project root and its mutation locks.
            /// </summary>
            public global::LocalAutomation.Runtime.Workspace PreparedWorkspace { get; }

            /// <summary>
            /// Gets the prepared persistent workspace project used by editor, standalone, and package steps.
            /// </summary>
            public Project ExampleProject { get; }

            /// <summary>
            /// Gets the session-scoped temporary root for disposable source extraction and transient launch outputs.
            /// </summary>
            public string TempPath { get; }

            /// <summary>
            /// Gets the session-scoped root for staged package output and cooked package data.
            /// </summary>
            public string PackageOutputPath { get; }
        }

        /// <summary>
        /// Validates that deployment verification has a concrete example-project archive root before any authored tasks are
        /// allowed to run.
        /// </summary>
        protected override string? CheckRequirementsSatisfied(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            string exampleProjectsPath = GetNormalizedExampleProjectsPath(operationParameters);
            if (string.IsNullOrWhiteSpace(exampleProjectsPath))
            {
                return "Example Projects Path is required.";
            }

            if (!Directory.Exists(exampleProjectsPath))
            {
                return $"Example Projects Path does not exist: {exampleProjectsPath}";
            }

            return null;
        }

        /// <summary>
        /// Describes the deployment-verification subtree beneath the framework-owned root task.
        /// </summary>
        protected override void DescribeExecutionPlan(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters, global::LocalAutomation.Runtime.ExecutionTaskBuilder root)
        {
            Plugin plugin = GetRequiredTarget(operationParameters);
            IReadOnlyList<EngineVersion> enabledVersions = operationParameters.GetOptions<EngineVersionOptions>().EnabledVersions;
            List<EngineVersion> targetVersions = enabledVersions.Count > 0
                ? enabledVersions.ToList()
                : new List<EngineVersion> { plugin.EngineInstance.Version };
            AutomationOptions automationOptions = operationParameters.GetOptions<AutomationOptions>();
            ConcurrentDictionary<EngineVersion, VerificationState> verificationStates = new();

            // Each engine branch prepares its own persistent workspace and uses per-engine UAT locks, so branches can
            // start independently while direct UBT builds still serialize on the shared Unreal build lock when needed.
            root.Children(global::LocalAutomation.Runtime.ExecutionChildMode.Parallel, engines =>
            {
                foreach (EngineVersion engineVersion in targetVersions)
                {
                    EngineVersion currentEngineVersion = engineVersion;
                    engines
                        .Task($"UE {currentEngineVersion.MajorMinorString}", "Per-engine verification scope")
                        .Children(steps => steps
                            .Task("Prepare")
                                .Describe("Resolve the installed plugin and extract the matching example project archive")
                                .Run(context => PrepareVerificationState(context, currentEngineVersion, verificationStates))
                            .Then("Build Editor Target")
                                .Describe("Build the prepared verification project editor target before launch and package-only UAT")
                                .WithExecutionLocks(_ => GetVerificationState(verificationStates, currentEngineVersion).PreparedWorkspace.MutationLocks
                                    .Append(UnrealExecutionLocks.GlobalBuild))
                                .Run(context => BuildEditorTargetAsync(context, GetVerificationState(verificationStates, currentEngineVersion)))
                            .Then("Test Editor")
                                .Describe("Run the editor verification pass")
                                .WithExecutionLocks(_ => GetVerificationState(verificationStates, currentEngineVersion).PreparedWorkspace.MutationLocks)
                                .When(automationOptions.RunTests, "Run Tests is off.")
                                .Run(context => TestEditorAsync(context, GetVerificationState(verificationStates, currentEngineVersion)))
                            .Then("Test Standalone")
                                .Describe("Run the standalone verification pass")
                                .WithExecutionLocks(_ => GetVerificationState(verificationStates, currentEngineVersion).PreparedWorkspace.MutationLocks)
                                .When(automationOptions.RunTests, "Run Tests is off.")
                                .Run(context => TestStandaloneAsync(context, GetVerificationState(verificationStates, currentEngineVersion)))
                            .Then("Build Package Target")
                                .Describe("Build the prepared verification project target before the package-only UAT pass")
                                .WithExecutionLocks(_ => GetVerificationState(verificationStates, currentEngineVersion).PreparedWorkspace.MutationLocks
                                    .Append(UnrealExecutionLocks.GlobalBuild))
                                .Run(context => BuildPackageTargetAsync(context, GetVerificationState(verificationStates, currentEngineVersion)))
                            .Then("Package Project")
                                .Describe("Cook, stage, pak, and package the prepared verification project without holding the Unreal build lock")
                                .WithExecutionLocks(_ =>
                                {
                                    VerificationState state = GetVerificationState(verificationStates, currentEngineVersion);
                                    return state.PreparedWorkspace.MutationLocks
                                        .Append(UnrealExecutionLocks.GetAutomationToolLock(state.Engine));
                                })
                                .Run(context => PackageProjectAsync(context, GetVerificationState(verificationStates, currentEngineVersion)))
                            .Then("Test Package")
                                .Describe("Run the packaged project verification pass")
                                .When(automationOptions.RunTests, "Run Tests is off.")
                                .Run(context => TestPackageAsync(context, GetVerificationState(verificationStates, currentEngineVersion))));
                }
            });
        }

        /// <summary>
        /// Deployment verification always needs engine-version selection, automation toggles, and example-project
        /// settings to know which builds to verify and how deeply to test them.
        /// </summary>
        protected override System.Collections.Generic.IEnumerable<System.Type> GetDeclaredOptionSetTypes(global::LocalAutomation.Runtime.IOperationTarget target)
        {
            return base.GetDeclaredOptionSetTypes(target)
                .Concat(new[]
                {
                    typeof(EngineVersionOptions),
                    typeof(AutomationOptions),
                    typeof(VerifyDeploymentOptions)
                });
        }

        /// <summary>
        /// Extracts the selected example archive into a session source copy and refreshes the persistent verification
        /// workspace project that every later project operation uses.
        /// </summary>
        private async Task PrepareVerificationState(global::LocalAutomation.Runtime.ExecutionTaskContext context, EngineVersion engineVersion, ConcurrentDictionary<EngineVersion, VerificationState> verificationStates)
        {
            global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters = context.ValidatedOperationParameters;
            Plugin plugin = GetRequiredTarget(operationParameters);
            Engine engine = EngineFinder.GetEngineInstall(engineVersion)
                ?? throw new Exception($"Engine {engineVersion.MajorMinorString} not found");
            EngineVersion resolvedEngineVersion = engine.Version ?? throw new Exception("Engine version is not available");
            context.Logger.LogInformation($"Verifying plugin {plugin.Name} for {resolvedEngineVersion.MajorMinorString}");

            UnrealUtilities.Plugin installedPlugin = DeploymentVerification.ValidateInstalledPlugin(plugin.Model, engine);
            string pluginVersionName = plugin.Model.PluginDescriptor.VersionName;
            context.Logger.LogInformation($"Source plugin version: {pluginVersionName}");
            string installedPluginVersionName = (installedPlugin.PluginDescriptor ?? throw new Exception("Installed plugin descriptor is not loaded")).VersionName;
            context.Logger.LogInformation($"Installed plugin version: {installedPluginVersionName}");

            string exampleProjects = GetNormalizedExampleProjectsPath(operationParameters);
            string exampleProjectZip = DeploymentVerification.FindExampleProjectZip(plugin.Model, exampleProjects, engine);

            context.Logger.LogInformation($"Identified {exampleProjectZip} as best example project");

            // Parallel engine branches must never share disposable session paths because archive extraction and package
            // cleanup delete and recreate branch-owned files while sibling branches are running.
            string temp = GetEngineBranchTempPath(context, resolvedEngineVersion);
            string exampleProjectSourcePath = Path.Combine(temp, "ExampleProjectSource");

            // Archive inspection supplies a standalone model; only the prepared operation target needs a watcher.
            UnrealUtilities.Project sourceProject = DeploymentVerification.ExtractExampleProject(
                exampleProjectZip, exampleProjectSourcePath, context.Logger, context.CancellationToken);

            global::LocalAutomation.Runtime.Workspace preparedWorkspace = global::LocalAutomation.Runtime.Workspaces.Persistent(UnrealWorkspaceKeys.ProjectWorkspace(
                engine,
                sourceProject,
                configuration: BuildConfiguration.Development,
                compiler: UbtCompiler.Default,
                cppStandard: UbtCppStandard.Default));

            preparedWorkspace.EnsureReady(context.Logger);
            Project exampleProject = new(DeploymentVerification.PrepareProject(
                sourceProject, preparedWorkspace.RootPath, context.Logger, context.CancellationToken));

            string packageOutput = Path.Combine(temp, "Package");

            // State is keyed per branch because Verify Deployment authors every engine branch under one operation root.
            // Root-scoped operation data would let the last prepared branch overwrite sibling branches while they run.
            verificationStates[engineVersion] = new VerificationState(plugin, engine, preparedWorkspace, exampleProject, temp, packageOutput);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Returns the prepared state for one authored engine branch or fails when a dependent task starts out of order.
        /// </summary>
        private static VerificationState GetVerificationState(ConcurrentDictionary<EngineVersion, VerificationState> verificationStates, EngineVersion engineVersion)
        {
            if (verificationStates.TryGetValue(engineVersion, out VerificationState? state))
            {
                return state;
            }

            throw new InvalidOperationException($"Verification state for UE {engineVersion.MajorMinorString} is not available. The Prepare task must complete before dependent verification tasks run.");
        }

        /// <summary>
        /// Returns the session-scoped temp root owned by one engine branch in a parallel verification run.
        /// </summary>
        private string GetEngineBranchTempPath(global::LocalAutomation.Runtime.ExecutionTaskContext context, EngineVersion engineVersion)
        {
            // Major/minor engine labels are stable, filesystem-safe branch identifiers for one Verify Deployment session.
            return Path.Combine(GetOperationTempPath(context), $"UE_{engineVersion.MajorMinorString}");
        }

        /// <summary>
        /// Launches the prepared verification project in the editor and runs the configured automation checks.
        /// </summary>
        private async Task TestEditorAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context, VerificationState state)
        {
            AutomationOptions automationOptions = context.ValidatedOperationParameters.GetOptions<AutomationOptions>();

            context.Logger.LogInformation("Launching and testing example project editor");
            await RunExampleProjectOperationAsync<LaunchProjectEditor>(state, automationOptions, context, "Failed to launch example project");
        }

        /// <summary>
        /// Launches the prepared verification project as a standalone game and runs the configured automation checks.
        /// </summary>
        private async Task TestStandaloneAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context, VerificationState state)
        {
            AutomationOptions automationOptions = context.ValidatedOperationParameters.GetOptions<AutomationOptions>();

            context.Logger.LogInformation("Launching and testing standalone");
            await RunExampleProjectOperationAsync<LaunchStandalone>(state, automationOptions, context, "Failed to launch standalone");
        }

        /// <summary>
        /// Builds the editor target directly so launch validation and package-only UAT have an editor receipt available.
        /// </summary>
        private async Task BuildEditorTargetAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context, VerificationState state)
        {
            // BuildEditorTarget uses Build.bat, so this step refreshes editor binaries without entering UAT.
            global::LocalAutomation.Runtime.OperationParameters buildParameters = CreateExampleProjectParams(state, outputPathOverride: Path.Combine(state.TempPath, "BuildEditorTarget"));
            buildParameters.GetOptions<BuildConfigurationOptions>().Configuration = BuildConfiguration.Development;
            await RunChildOperationAsync(new BuildEditorTarget(), buildParameters, context, required: true, failureMessage: "Failed to build example project editor target", hideChildOperationRootInGraph: true);
        }

        /// <summary>
        /// Builds the game target directly so package-only BuildCookRun can hold only the AutomationTool lock.
        /// </summary>
        private async Task BuildPackageTargetAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context, VerificationState state)
        {
            // BuildProjectTarget uses Build.bat, so this step acquires the shared Unreal build lock without entering UAT.
            global::LocalAutomation.Runtime.OperationParameters buildParameters = CreateExampleProjectParams(state, outputPathOverride: Path.Combine(state.TempPath, "BuildPackageTarget"));
            buildParameters.GetOptions<BuildConfigurationOptions>().Configuration = BuildConfiguration.Development;
            buildParameters.GetOptions<AdditionalArgumentsOptions>().Arguments = "-nocompileeditor";
            await RunChildOperationAsync(new BuildProjectTarget(), buildParameters, context, required: true, failureMessage: "Failed to build example project package target", hideChildOperationRootInGraph: true);
        }

        /// <summary>
        /// Runs a package-only BuildCookRun pass for the prepared verification project with session-scoped package outputs.
        /// </summary>
        private async Task PackageProjectAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context, VerificationState state)
        {
            // Package and cook outputs are owned by this execution session; clearing them before UAT starts makes package
            // discovery represent the current verification run only.
            ProjectPackaging.PreparePackageOutputs(state.ExampleProject.Model, state.PackageOutputPath,
                context.Logger, context.CancellationToken);

            // UAT appends the platform beneath -stagingdirectory, while -CookOutputDir names the final platform folder.
            string stagingRootPath = ProjectPackaging.GetStagingRootPath(state.PackageOutputPath);
            string cookOutputPath = ProjectPackaging.GetCookOutputPath(state.PackageOutputPath, state.Engine);

            global::LocalAutomation.Runtime.OperationParameters packageParameters = CreateExampleProjectParams(state, outputPathOverride: state.PackageOutputPath);
            packageParameters.GetOptions<AdditionalArgumentsOptions>().Arguments = "-nocompileeditor";
            packageParameters.GetOptions<BuildConfigurationOptions>().Configuration = BuildConfiguration.Development;
            // The target build runs before this task, so BuildCookRun can skip compilation and keep UAT serialization
            // independent from the shared Unreal build lock.
            BuildCookRunProjectRequest request = ProjectPackaging.CreatePackageOnlyRequest(
                BuildConfiguration.Development, stagingRootPath, cookOutputPath);
            await RunChildOperationAsync(new ConfiguredBuildCookRunProjectOperation("Package Example Project", request), packageParameters, context, required: true, failureMessage: "Failed to package example project", hideChildOperationRootInGraph: true);
        }

        /// <summary>
        /// Launches the packaged build produced in the current session-scoped staging root.
        /// </summary>
        private async Task TestPackageAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context, VerificationState state)
        {
            AutomationOptions automationOptions = context.ValidatedOperationParameters.GetOptions<AutomationOptions>();

            // The package target points at the session staging output instead of the persistent project Saved directory so
            // launch validation cannot accidentally discover a package from a previous workspace use.
            using Package package = new(ProjectPackaging.GetRequiredPackagePath(
                state.PackageOutputPath, state.Engine, "Package output is not available for launch"));
            global::LocalAutomation.Runtime.OperationParameters packageParameters = CreateParameters();
            packageParameters.Target = package;
            packageParameters.OutputPathOverride = Path.Combine(state.TempPath, "PackageLaunch");
            packageParameters.GetOptions<EngineVersionOptions>().EnabledVersions = new[] { state.Engine.Version };
            packageParameters.SetOptions(automationOptions);
            await RunChildOperationAsync<LaunchPackage>(packageParameters, context, required: true, failureMessage: "Launch and test package failed", hideChildOperationRootInGraph: true);

            context.Logger.LogInformation($"Finished verifying plugin {state.SourcePlugin.Name} for {state.Engine.Version.MajorMinorString}");
        }

        /// <summary>
        /// Creates child-operation parameters for operations that directly target the prepared verification project.
        /// </summary>
        private global::LocalAutomation.Runtime.OperationParameters CreateExampleProjectParams(VerificationState state, AutomationOptions? automationOptions = null, string? outputPathOverride = null)
        {
            global::LocalAutomation.Runtime.OperationParameters parameters = CreateParameters();
            parameters.Target = state.ExampleProject;
            parameters.OutputPathOverride = outputPathOverride;
            parameters.GetOptions<EngineVersionOptions>().EnabledVersions = new[] { state.Engine.Version };
            if (automationOptions != null)
            {
                parameters.SetOptions(automationOptions);
            }

            return parameters;
        }

        /// <summary>
        /// Runs one project-targeted child operation against the prepared verification project.
        /// </summary>
        private Task RunExampleProjectOperationAsync<TOperation>(VerificationState state, AutomationOptions automationOptions, global::LocalAutomation.Runtime.ExecutionTaskContext context, string failureMessage)
            where TOperation : global::LocalAutomation.Runtime.Operation, new()
        {
            return RunChildOperationAsync<TOperation>(CreateExampleProjectParams(state, automationOptions), context, required: true, failureMessage: failureMessage, hideChildOperationRootInGraph: true);
        }

        /// <summary>
        /// Returns the trimmed example-project archive root so requirement checks and task execution interpret the same
        /// user-provided path.
        /// </summary>
        private static string GetNormalizedExampleProjectsPath(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters)
        {
            return operationParameters.GetOptions<VerifyDeploymentOptions>().ExampleProjectsPath.Trim();
        }

    }
}
