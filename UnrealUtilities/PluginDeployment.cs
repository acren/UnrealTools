using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FileMaterialization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SystemUtilities.IO;

namespace UnrealUtilities
{
    /// <summary>Prepares deployment source, isolated Unreal projects, installed plugins and distribution archives.</summary>
    public static class PluginDeployment
    {
        // Validation and cooking share a graph that leaves the Common Zen service available to concurrent package jobs.
        private const string DeploymentDdcGraph = "InstalledNoZenLocalFallback";

        /// <summary>Creates deployment validation flags with secondary-process semantics only for editor-based launches.</summary>
        public static Arguments CreateValidationLaunchArguments(bool editorProcess)
        {
            Arguments arguments = new();
            // Deploy validation launches are controlled automation runs, so Unreal messaging stays disabled.
            arguments.SetFlag("NoMessaging");
            /* Validation launches use the installed-engine DDC graph that skips the local Zen backend so they do not
               restart the shared Common Zen service while package jobs are staging IoStore data. */
            arguments.SetKeyValue("ddc", DeploymentDdcGraph);
            if (editorProcess)
            {
                // Editor, editor-as-game and commandlet validation all receive Unreal's secondary-process semantics.
                arguments.SetFlag("Multiprocess");
            }

            return arguments;
        }

        /// <summary>
        /// Creates the explicit package-only BuildCookRun request used by prepared deployment branches. Caller-supplied
        /// staging and cook roots keep package artifacts out of persistent project workspaces, while cooker arguments select
        /// the installed no-Zen DDC graph used by validation processes.
        /// </summary>
        public static BuildCookRunProjectRequest CreatePreparedProjectPackageRequest(BuildConfiguration configuration,
            string stagingDirectory, string cookOutputDirectory, bool noDebugInfo = false)
        {
            return ProjectPackaging.CreatePackageOnlyRequest(configuration, stagingDirectory, cookOutputDirectory,
                noDebugInfo: noDebugInfo, additionalCookerOptions: $"-ddc={DeploymentDdcGraph}");
        }

        /// <summary>Requires host enablement and stamps plugin integer version, source copyright and project version.</summary>
        public static void PrepareSharedSource(Plugin plugin, Project hostProject, ILogger logger, CancellationToken cancellationToken = default)
        {
            PluginDescriptor pluginDescriptor = plugin.PluginDescriptor;
            if (!hostProject.ProjectDescriptor.HasPluginEnabled(plugin.Name))
            {
                throw new Exception("Host project must have plugin enabled");
            }

            int version = pluginDescriptor.SemVersion.ToInt();
            logger.LogInformation($"Version '{pluginDescriptor.VersionName}' -> {version}");
            bool updated = plugin.UpdateVersionInteger();
            logger.LogInformation(updated ? "Updated .uplugin version from name" : ".uplugin already has correct version");

            string? copyrightNotice = hostProject.GetCopyrightNotice();
            if (copyrightNotice == null)
            {
                throw new Exception("Project should have a copyright notice");
            }

            // Each source file uses the project notice as its first line; an existing leading comment is replaced.
            string sourcePath = Path.Combine(plugin.PluginPath, "Source");
            string expectedComment = $"// {copyrightNotice}";
            foreach (string file in Directory.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? firstLine;
                using (StreamReader reader = new(file))
                {
                    firstLine = reader.ReadLine();
                }

                if (firstLine == expectedComment)
                {
                    continue;
                }

                List<string> lines = File.ReadAllLines(file).ToList();
                if (firstLine != null && firstLine.StartsWith("//", StringComparison.Ordinal))
                {
                    lines[0] = expectedComment;
                }
                else
                {
                    lines.Insert(0, expectedComment);
                }

                File.WriteAllLines(file, lines);
                string relativePath = Path.GetRelativePath(sourcePath, file);
                logger.LogInformation($"Updated copyright notice: {relativePath}");
            }

            hostProject.SetProjectVersion(plugin.PluginDescriptor.VersionName);
            logger.LogInformation($"Updated project version to {plugin.PluginDescriptor.VersionName}");
        }

        /// <summary>Materializes selected host inputs and the target plugin, validates the layout and stamps engine association.</summary>
        public static Project PrepareWorkspace(Plugin plugin, Project hostProject, Engine engine, string workspaceProjectPath,
            bool includeOtherPlugins, string excludePlugins, ILogger logger, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(hostProject.PluginsPath))
            {
                throw new DirectoryNotFoundException($"Host project is missing required Plugins directory: {hostProject.PluginsPath}");
            }

            logger.LogInformation($"Source Plugins directory: {hostProject.PluginsPath}");
            IReadOnlySet<string> includedSiblingPluginNames = GetIncludedSiblingPluginNames(hostProject, plugin.Name, includeOtherPlugins, excludePlugins);
            logger.LogInformation($"Copying host project to workspace: {workspaceProjectPath}");
            FileMaterializer.MaterializeDirectory(hostProject.ProjectPath, workspaceProjectPath, MaterializationSpecs.CreateProject(hostProject, includedSiblingPluginNames),
                message => logger.LogInformation("{Message}", message), cancellationToken);

            // The target plugin is always included, independently of sibling selection.
            string workspacePluginsPath = Path.Combine(workspaceProjectPath, "Plugins");
            string workspacePluginPath = Path.Combine(workspacePluginsPath, plugin.Name);
            Directory.CreateDirectory(workspacePluginsPath);
            logger.LogInformation($"Materializing target plugin into workspace: {workspacePluginPath}");
            FileMaterializer.MaterializeDirectory(plugin.PluginPath, workspacePluginPath, MaterializationSpecs.CreatePlugin(plugin),
                message => logger.LogInformation("{Message}", message), cancellationToken);
            logger.LogInformation($"Finished copying host project to workspace: {workspaceProjectPath}");
            if (!Directory.Exists(workspacePluginsPath))
            {
                throw new DirectoryNotFoundException($"Workspace project copy is missing Plugins directory after copy: {workspacePluginsPath}");
            }

            logger.LogInformation($"Workspace Plugins directory: {workspacePluginsPath}");
            if (!ProjectPaths.Instance.IsTargetDirectory(workspaceProjectPath))
            {
                throw new InvalidOperationException($"Workspace project copy is not valid after copy: {workspaceProjectPath}");
            }
            if (!PluginPaths.Instance.IsTargetDirectory(workspacePluginPath))
            {
                throw new InvalidOperationException($"Could not find the target plugin inside the workspace project: {workspacePluginPath}");
            }

            Project workspaceProject = new(workspaceProjectPath);
            Plugin workspacePlugin = new(workspacePluginPath);
            logger.LogInformation($"Resolved workspace plugin: {workspacePlugin.PluginPath}");
            UpdateProjectDescriptorForArchive(workspaceProject, engine.Version);
            logger.LogInformation("Updated workspace project descriptor for archive output");
            return workspaceProject;
        }

        /// <summary>Stages and stamps the target plugin, flattens resolved inputs and returns merged sibling names.</summary>
        public static IReadOnlySet<string> StagePlugin(Plugin sourcePlugin, Plugin workspacePlugin, Engine engine,
            string stagingPluginPath, string packageInputPluginPath,
            IReadOnlyList<UnrealPluginFlattening.MergePlugin> mergePlugins, ILogger logger, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(mergePlugins);
            FileUtils.DeleteDirectoryIfExists(stagingPluginPath);
            FileMaterializer.MaterializeDirectory(workspacePlugin.PluginPath, stagingPluginPath, MaterializationSpecs.CreatePlugin(workspacePlugin),
                message => logger.LogInformation("{Message}", message), cancellationToken);
            logger.LogInformation($"Copied plugin to staging destination: {stagingPluginPath}");
            if (!PluginPaths.Instance.IsTargetDirectory(stagingPluginPath))
            {
                throw new InvalidOperationException($"Staged plugin was not created successfully: {stagingPluginPath}");
            }

            Plugin stagingPlugin = new(stagingPluginPath);
            UpdatePluginDescriptorForArchive(sourcePlugin.PluginDescriptor, stagingPlugin, engine.Version);
            logger.LogInformation("Refreshing BuildPlugin host plugin input from '{StagingPluginPath}' to '{PackageInputPluginPath}'.", stagingPlugin.PluginPath, packageInputPluginPath);
            if (mergePlugins.Count > 0)
            {
                logger.LogInformation("Flattening {MergePluginCount} merge plugin(s) into staged plugin '{PluginName}'.", mergePlugins.Count, stagingPlugin.Name);
            }

            // The flattener owns source validation and rewriting; its generated output is the persistent BuildPlugin input.
            UnrealPluginFlattening.PluginFlattener.Flatten(stagingPlugin.UPluginPath, mergePlugins, packageInputPluginPath,
                message => logger.LogInformation("{Message}", message), cancellationToken);
            /* Archive creation needs an isolated source-only snapshot, while the persistent package input retains generated
               build directories across runs. Mirror only the generated output for that archive-facing staging role. */
            FileMaterializer.MaterializeDirectory(packageInputPluginPath, stagingPlugin.PluginPath,
                MaterializationSpecs.CreatePlugin(packageInputPluginPath), message => logger.LogInformation("{Message}", message), cancellationToken, mirrorDirectories: true);
            stagingPlugin.LoadDescriptor();
            logger.LogInformation($"Updated plugin descriptor for staging: {stagingPlugin.PluginDescriptor.VersionName}");
            return mergePlugins
                .Select(mergePlugin => Path.GetFileNameWithoutExtension(mergePlugin.DescriptorPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Builds the distribution filename prefix using plugin version, engine version and nonstandard branch suffix.</summary>
        public static string BuildArchivePrefix(Plugin plugin, EngineVersion engineVersion, string? branchName)
        {
            PluginDescriptor pluginDescriptor = plugin.PluginDescriptor;
            bool standardBranch = true;
            if (!string.IsNullOrEmpty(branchName))
            {
                string[] standardBranchNames = { "master", "develop", "development" };
                string[] standardBranchPrefixes = { "version/", "release/", "hotfix/" };
                standardBranch = standardBranchNames.Contains(branchName, StringComparer.InvariantCultureIgnoreCase) ||
                                 standardBranchPrefixes.Any(prefix => branchName.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase));
            }

            string archivePrefix = plugin.Name;
            if (pluginDescriptor.IsBetaVersion)
            {
                archivePrefix += "_beta";
            }

            string pluginVersionString = pluginDescriptor.VersionName;
            string fullPluginVersionString = pluginVersionString;
            if (!string.IsNullOrEmpty(branchName) &&
                !pluginDescriptor.VersionName.Contains(branchName) &&
                !engineVersion.ToString().Contains(branchName) &&
                !standardBranch)
            {
                fullPluginVersionString = $"{pluginVersionString}-{branchName.Replace("/", "-")}";
            }

            archivePrefix += $"_{fullPluginVersionString}";
            archivePrefix += $"_UE{engineVersion.MajorMinorString}";
            archivePrefix += "_";
            return archivePrefix;
        }

        /// <summary>Prepares the project-plugin base with selected unmerged siblings, engine association and deployment version.</summary>
        public static Project MaterializeProjectPluginBase(Plugin sourcePlugin, Project hostProject, Project workspaceProject,
            Engine engine, string exampleProjectPath, bool includeOtherPlugins, string excludePlugins,
            IReadOnlySet<string> mergedPluginNames, ILogger logger, CancellationToken cancellationToken = default)
        {
            IReadOnlySet<string> includedSiblingPluginNames = GetIncludedSiblingPluginNames(hostProject, sourcePlugin.Name, includeOtherPlugins, excludePlugins, mergedPluginNames);
            FileMaterializer.MaterializeDirectory(workspaceProject.ProjectPath, exampleProjectPath, MaterializationSpecs.CreateProject(workspaceProject, includedSiblingPluginNames),
                message => logger.LogInformation("{Message}", message), cancellationToken, mirrorDirectories: true);
            if (!ProjectPaths.Instance.IsTargetDirectory(exampleProjectPath))
            {
                throw new InvalidOperationException($"Project-plugin base was not materialized successfully: {exampleProjectPath}");
            }

            Project exampleProject = new(exampleProjectPath);
            UpdateProjectDescriptorForArchive(exampleProject, engine.Version);
            logger.LogInformation($"Updated project descriptor for archive: EngineAssociation = {engine.Version}");
            string exampleProjectVersion = ProjectConfig.BuildVersionWithEnginePrefix(sourcePlugin.PluginDescriptor.VersionName, engine.Version);
            exampleProject.SetProjectVersion(exampleProjectVersion);
            logger.LogInformation($"Updated project version to {exampleProjectVersion}");
            return exampleProject;
        }

        /// <summary>Installs packaged plugin outputs into a prepared project so validation exercises the distributable payload.</summary>
        public static Plugin InstallDistributablePluginIntoProject(Plugin builtPlugin, Project project, ILogger logger, CancellationToken cancellationToken = default)
        {
            string installedPluginPath = Path.Combine(project.PluginsPath, builtPlugin.Name);
            Directory.CreateDirectory(project.PluginsPath);
            FileMaterializer.MaterializeDirectory(builtPlugin.PluginPath, installedPluginPath, MaterializationSpecs.CreatePlugin(builtPlugin, includeBuildOutputs: true),
                message => logger.LogInformation("{Message}", message), cancellationToken, mirrorDirectories: true);
            if (!PluginPaths.Instance.IsTargetDirectory(installedPluginPath))
            {
                throw new InvalidOperationException($"Built plugin was not installed into the project-plugin base successfully: {installedPluginPath}");
            }

            Plugin installedPlugin = new(installedPluginPath);
            logger.LogInformation("Installed distributable plugin into project-plugin base: {InstalledPluginPath}", installedPlugin.PluginPath);
            return installedPlugin;
        }

        /// <summary>Copies prebuilt project/plugin inputs for Clang validation while preserving destination project build caches.</summary>
        public static Project PrepareClangVariant(Project sourceProject, string destinationPath, ILogger logger, CancellationToken cancellationToken = default)
        {
            Project variant = MaterializePrebuiltProjectVariant(sourceProject, destinationPath, "Clang validation variant was not created successfully", logger, cancellationToken);
            logger.LogInformation($"Prepared Clang validation variant: {variant.ProjectPath}");
            return variant;
        }

        /// <summary>Copies prebuilt inputs and removes the project plugin so packaging resolves its engine installation.</summary>
        public static Project PrepareEnginePluginVariant(Project sourceProject, string destinationPath, string pluginName, ILogger logger, CancellationToken cancellationToken = default)
        {
            Project variant = MaterializePrebuiltProjectVariant(sourceProject, destinationPath, "Engine-plugin variant was not created successfully", logger, cancellationToken);
            variant.RemovePlugin(pluginName);
            logger.LogInformation($"Prepared engine-plugin variant: {variant.ProjectPath}");
            return variant;
        }

        /// <summary>Copies prebuilt inputs, removes the project plugin and converts to blueprint-only while retaining siblings.</summary>
        public static Project PrepareBlueprintDemoVariant(Project sourceProject, string destinationPath, string pluginName, ILogger logger, CancellationToken cancellationToken = default)
        {
            Project variant = MaterializePrebuiltProjectVariant(sourceProject, destinationPath, "Blueprint/demo variant was not created successfully", logger, cancellationToken);
            variant.RemovePlugin(pluginName);
            variant.ConvertToBlueprintOnly();
            // Return descriptor state matching the prepared files so callers can immediately use blueprint-only metadata.
            variant.LoadDescriptor();
            logger.LogInformation($"Prepared blueprint/demo variant: {variant.ProjectPath}");
            return variant;
        }

        /// <summary>Materializes editor and plugin outputs while preserving each variant's own root project build-output cache.</summary>
        private static Project MaterializePrebuiltProjectVariant(Project sourceProject, string destinationPath, string failureMessage, ILogger logger, CancellationToken cancellationToken)
        {
            IReadOnlySet<string> includedPluginNames = MaterializationSpecs.GetProjectPluginNames(sourceProject);
            FileMaterializer.MaterializeDirectory(sourceProject.ProjectPath, destinationPath,
                MaterializationSpecs.CreateProject(sourceProject, includedPluginNames, includeProjectEditorBuildOutputs: true, includePluginBuildOutputs: true),
                message => logger.LogInformation("{Message}", message), cancellationToken, mirrorDirectories: true);
            if (!ProjectPaths.Instance.IsTargetDirectory(destinationPath))
            {
                throw new InvalidOperationException($"{failureMessage}: {destinationPath}");
            }

            return new Project(destinationPath);
        }

        /// <summary>Removes a same-named engine plugin before UBT scans the distributable plugin inputs.</summary>
        public static void RemoveExistingEnginePluginInstall(Engine engine, string pluginName, ILogger logger, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string installedPluginPath = EnginePathUtils.GetMarketplacePluginPath(engine, pluginName);
            if (!Directory.Exists(installedPluginPath))
            {
                logger.LogInformation("No existing engine plugin install found at {EnginePluginPath}.", installedPluginPath);
                return;
            }

            // UBT chooses one descriptor for duplicate plugin names while compiling the command-line plugin, so a stale
            // engine install with the same name must be hidden before the distributable package build begins.
            logger.LogInformation("Removing existing engine plugin install before distributable package build: {EnginePluginPath}", installedPluginPath);
            FileUtils.DeleteDirectoryIfExists(installedPluginPath, logger);
        }

        /// <summary>Replaces the engine Marketplace plugin directory with the complete built payload.</summary>
        public static Plugin InstallBuiltPluginToEngine(Plugin builtPlugin, Engine engine, ILogger logger, CancellationToken cancellationToken = default)
        {
            string installedPluginPath = EnginePathUtils.GetMarketplacePluginPath(engine, builtPlugin.Name);
            logger.LogInformation($"Copying plugin to {installedPluginPath}");
            FileUtils.DeleteDirectoryIfExists(installedPluginPath);
            DirectoryMaterializer.Copy(builtPlugin.PluginPath, installedPluginPath, cancellationToken: cancellationToken);
            return new Plugin(installedPluginPath);
        }

        /// <summary>Resets an explicit project path and creates a descriptor-only project enabling the engine plugin.</summary>
        public static Project CreateEmptyEnginePluginProject(string projectPath, string projectName, Engine engine, string pluginName, ILogger logger, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Each validation starts from a descriptor-only project shell.
            FileUtils.DeleteDirectoryIfExists(projectPath, logger);
            Project project = Project.CreateEmpty(projectPath, projectName, engine.Version);
            project.SetPluginEnabled(pluginName, true);
            return project;
        }

        /// <summary>Archives a dedicated project copy, retaining sibling plugin outputs and pruning root outputs and PDB files.</summary>
        public static void CreateExampleProjectArchive(Project blueprintVariant, string archiveProjectPath, string exampleProjectZipPath, ILogger logger, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exampleProjectZipPath))!);
            FileUtils.DeleteDirectoryIfExists(archiveProjectPath, logger);
            // Source example archives omit root project binaries but must keep each code plugin's packaged module outputs.
            FileMaterializationSpec archiveProjectSpec = MaterializationSpecs.CreateProject(blueprintVariant, MaterializationSpecs.GetProjectPluginNames(blueprintVariant), includePluginBuildOutputs: true);
            FileMaterializer.MaterializeDirectory(blueprintVariant.ProjectPath, archiveProjectPath, archiveProjectSpec,
                message => logger.LogInformation("{Message}", message), cancellationToken, mirrorDirectories: true);
            if (!ProjectPaths.Instance.IsTargetDirectory(archiveProjectPath))
            {
                throw new InvalidOperationException($"Example-project archive copy is not available: {archiveProjectPath}");
            }

            Project archiveProject = new(archiveProjectPath);
            string[] allowedExampleProjectSubDirectoryNames = { "Content", "Config", "Plugins" };
            FileUtils.DeleteOtherSubdirectories(archiveProject.ProjectPath, allowedExampleProjectSubDirectoryNames);
            FileUtils.DeleteFilesWithExtension(archiveProject.ProjectPath, new[] { ".pdb" }, SearchOption.AllDirectories);
            FileUtils.DeleteFileIfExists(exampleProjectZipPath);
            FileUtils.CreateZipFromDirectory(archiveProject.ProjectPath, exampleProjectZipPath, false, logger);
        }

        /// <summary>Resolves siblings that remain separate project plugins during deployment validation.</summary>
        private static IReadOnlySet<string> GetIncludedSiblingPluginNames(Project referenceProject, string targetPluginName,
            bool includeOtherPlugins, string excludePlugins, IReadOnlySet<string>? additionallyExcludedPluginNames = null)
        {
            HashSet<string> excludedPluginNames = GetExcludedPluginNames(excludePlugins);
            if (additionallyExcludedPluginNames != null)
            {
                excludedPluginNames.UnionWith(additionallyExcludedPluginNames);
            }

            if (!includeOtherPlugins)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            // Name selection reads standalone plugin models; runtime target lifetime belongs to operation entry points.
            return referenceProject.Plugins
                .Where(plugin => !plugin.Name.Equals(targetPluginName, StringComparison.OrdinalIgnoreCase))
                .Where(plugin => !excludedPluginNames.Contains(plugin.Name))
                .Select(plugin => plugin.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Parses comma-delimited sibling exclusions into case-insensitive plugin names.</summary>
        private static HashSet<string> GetExcludedPluginNames(string excludePlugins)
        {
            return excludePlugins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Stamps archive plugin and engine versions while retaining unmodeled descriptor fields.</summary>
        private static void UpdatePluginDescriptorForArchive(PluginDescriptor sourceDescriptor, Plugin plugin, EngineVersion engineVersion)
        {
            JObject pluginDescriptor = JObject.Parse(File.ReadAllText(plugin.UPluginPath));
            bool modified = false;
            // Check version name - use same format as example project
            string desiredVersionName = ProjectConfig.BuildVersionWithEnginePrefix(sourceDescriptor.VersionName, engineVersion);
            modified |= pluginDescriptor.Set("VersionName", desiredVersionName);
            // Check engine version
            EngineVersion desiredEngineMajorMinorVersion = engineVersion.WithPatch(0);
            modified |= pluginDescriptor.Set("EngineVersion", desiredEngineMajorMinorVersion.ToString());
            if (modified)
            {
                File.WriteAllText(plugin.UPluginPath, pluginDescriptor.ToString());
            }
        }

        /// <summary>Stamps major/minor engine association while retaining unmodeled project descriptor fields.</summary>
        private static void UpdateProjectDescriptorForArchive(Project project, EngineVersion engineVersion)
        {
            JObject projectDescriptor = JObject.Parse(File.ReadAllText(project.UProjectPath));
            bool modified = false;
            // Check engine association - use major.minor format
            string desiredEngineAssociation = engineVersion.MajorMinorString;
            modified |= projectDescriptor.Set("EngineAssociation", desiredEngineAssociation);
            if (modified)
            {
                File.WriteAllText(project.UProjectPath, projectDescriptor.ToString());
                project.LoadDescriptor();
            }
        }
    }
}
