using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using FileMaterialization;
using Microsoft.Extensions.Logging;
using Semver;
using SystemUtilities.IO;

namespace UnrealUtilities;

/// <summary>Validates installed distributions and selects, extracts and prepares compatible example-project inputs.</summary>
public static class DeploymentVerification
{
    /// <summary>Requires an installed plugin whose version text includes the supplied source version.</summary>
    public static Plugin ValidateInstalledPlugin(Plugin sourcePlugin, Engine engine)
    {
        Plugin installed = engine.FindInstalledPlugin(sourcePlugin.Name)
            ?? throw new Exception($"Could not find plugin {sourcePlugin.Name} in engine located at {engine.TargetPath}");
        string sourceVersion = sourcePlugin.PluginDescriptor.VersionName;
        string installedVersion = installed.PluginDescriptor.VersionName;
        // Distribution version labels can contain the source label plus release-specific suffixes.
        if (!installedVersion.Contains(sourceVersion))
        {
            throw new Exception($"Installed plugin version {installedVersion} does not include reference version {sourceVersion}");
        }

        return installed;
    }

    /// <summary>Selects the newest example archive compatible with the source plugin and verification engine.</summary>
    public static string FindExampleProjectZip(Plugin plugin, string archiveRoot, Engine engine)
    {
        SemVersion pluginVersion = plugin.PluginDescriptor.SemVersion;
        string[] zipPaths = Directory.GetFiles(archiveRoot, "*.zip", SearchOption.AllDirectories);
        List<ExampleProjectZipInfo> candidates = new();
        foreach (string path in zipPaths)
        {
            ExampleProjectZipInfo info = new(path);
            if (info.IsExampleProject && info.PluginName == plugin.Name)
            {
                candidates.Add(info);
            }
        }

        if (candidates.Count == 0)
        {
            throw new Exception("No valid zips");
        }

        // Select only versions not newer than either reference, preferring the plugin version before engine version.
        return candidates
            .Where(z => z.PluginVersion != null && SemVersion.CompareSortOrder(z.PluginVersion, pluginVersion) <= 0 && z.EngineVersion != null && SemVersion.CompareSortOrder(z.EngineVersion, engine.SemVersion) <= 0)
            .OrderByDescending(z => z.PluginVersion)
            .ThenByDescending(z => z.EngineVersion)
            .First().Path;
    }

    /// <summary>Resets a disposable source directory and extracts a required example project from its archive.</summary>
    public static Project ExtractExampleProject(string archivePath, string sourcePath, ILogger logger, CancellationToken cancellationToken)
    {
        /* Extract into a disposable source so refreshing a persistent destination can preserve its own generated
           outputs. ZIP extraction and deletion are synchronous; cancellation is observed between those steps. */
        cancellationToken.ThrowIfCancellationRequested();
        FileUtils.DeleteDirectoryIfExists(sourcePath, logger);
        cancellationToken.ThrowIfCancellationRequested();
        ZipFile.ExtractToDirectory(archivePath, sourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        Project sourceProject = new(sourcePath);
        if (!sourceProject.IsValid)
        {
            throw new Exception($"Couldn't create project from {archivePath}");
        }

        return sourceProject;
    }

    /// <summary>Refreshes a verification project with archived plugin binaries while preserving destination build caches.</summary>
    public static Project PrepareProject(Project source, string destinationPath, ILogger logger, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlySet<string> includedPluginNames = MaterializationSpecs.GetProjectPluginNames(source);
        /* Archived project-plugin binaries let Unreal load enabled plugins before direct target builds refresh
           project-owned receipts and executables. The destination retains its own root build-output cache. */
        FileMaterializationSpec projectInputs = MaterializationSpecs.CreateProject(source, includedPluginNames, includePluginBuildOutputs: true);
        logger.LogInformation("Refreshing verification project workspace from '{SourceProjectPath}' to '{WorkspacePath}'.", source.ProjectPath, destinationPath);
        FileMaterializer.MaterializeDirectory(source.ProjectPath, destinationPath, projectInputs, logger, cancellationToken, mirrorDirectories: true);

        if (!ProjectPaths.Instance.IsTargetDirectory(destinationPath))
        {
            throw new InvalidOperationException($"Verification project workspace is not a valid project after refresh: {destinationPath}");
        }

        Project project = new(destinationPath);
        if (!project.IsValid)
        {
            throw new Exception($"Couldn't create project from prepared workspace {destinationPath}");
        }

        return project;
    }

    // Archive names carry release compatibility facts; unrelated ZIP files are not candidate projects.
    private readonly struct ExampleProjectZipInfo
    {
        public readonly string Path;
        public readonly string PluginName;
        public readonly SemVersion? PluginVersion;
        public readonly SemVersion? EngineVersion;
        public readonly bool IsExampleProject;

        /// <summary>Reads the fixed PluginName_PluginVersion_EngineVersion_ExampleProject archive convention.</summary>
        public ExampleProjectZipInfo(string path)
        {
            Path = path;
            PluginName = string.Empty;
            PluginVersion = null;
            EngineVersion = null;
            string zipName = System.IO.Path.GetFileNameWithoutExtension(path);
            string[] split = zipName.Split('_');
            IsExampleProject = split.Length > 3 && split[3] == "ExampleProject";
            if (!IsExampleProject)
            {
                return;
            }

            PluginName = split[0];
            PluginVersion = split.Length > 1 ? SemVersion.Parse(split[1], SemVersionStyles.Any) : null;
            EngineVersion = split.Length > 2 ? SemVersion.Parse(split[2].Replace("UE", ""), SemVersionStyles.Any) : null;
        }
    }
}
