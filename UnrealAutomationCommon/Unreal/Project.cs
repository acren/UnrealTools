using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace UnrealAutomationCommon.Unreal
{
    /// <summary>Owns project descriptor state and filesystem calculations for explicit utility use.</summary>
    public class Project : IEngineInstanceProvider
    {
        /// <summary>Gets the directory containing the project descriptor.</summary>
        public string TargetPath { get; }

        /// <summary>Gets the project directory used by configuration and content path utilities.</summary>
        public string TargetDirectory => TargetPath;

        /// <summary>Reads the descriptor from a project directory.</summary>
        [JsonConstructor]
        public Project(string targetPath)
        {
            if (!ProjectPaths.Instance.IsTargetDirectory(targetPath))
            {
                throw new ArgumentException($"Project '{targetPath}' does not contain a .uproject.", nameof(targetPath));
            }

            TargetPath = targetPath;

            LoadDescriptor();
        }

        /// <summary>
        /// Creates a descriptor-only Unreal project without modules, plugins, content assets, or source files.
        /// </summary>
        public static Project CreateEmpty(string projectPath, string projectName, EngineVersion? engineVersion = null)
        {
            // Generated projects start with only a descriptor so callers compose features such as plugin dependencies
            // explicitly after the project shell exists.
            string resolvedProjectPath = RequireText(projectPath, nameof(projectPath), "Project path is required.");
            string resolvedProjectName = RequireText(projectName, nameof(projectName), "Project name is required.");
            Directory.CreateDirectory(resolvedProjectPath);
            /* Unreal registers a watcher for the project content root during editor startup, so generated project shells
               include the directory even when no assets are authored into it. */
            Directory.CreateDirectory(Path.Combine(resolvedProjectPath, "Content"));
            ProjectDescriptor.CreateEmpty(engineVersion).Save(Path.Combine(resolvedProjectPath, resolvedProjectName + ".uproject"));
            return new Project(resolvedProjectPath);
        }

        public string UProjectPath => ProjectPaths.Instance.FindRequiredTargetFile(TargetPath);

        /// <summary>Gets the descriptor snapshot from construction or the last explicit reload.</summary>
        public ProjectDescriptor ProjectDescriptor { get; private set; } = null!;

        public Engine EngineInstance => ProjectDescriptor.Engine;

        public string EngineInstanceName
        {
            get
            {
                if (EngineInstance != null)
                {
                    return EngineInstance.DisplayName;
                }

                return "None";
            }
        }

        public string Name => Path.GetFileNameWithoutExtension(UProjectPath) ?? "Invalid";
        public string DisplayName => new DirectoryInfo(TargetPath).Name;

        public bool IsValid => ProjectPaths.Instance.IsTargetDirectory(TargetPath);

        public string ProjectPath => TargetPath;

        public string LogsPath => Path.Combine(ProjectPath, "Saved", "Logs");

        public string StagedBuildsPath => Path.Combine(ProjectPath, "Saved", "StagedBuilds");

        public string PluginsPath => Path.Combine(ProjectPath, "Plugins");

        public string SourcePath => Path.Combine(ProjectPath, "Source");

        /**
         * Plugins contained within the project directory
         * Does not include referenced plugins installed to the engine
         */
        public List<Plugin> Plugins
        {
            get
            {
                var plugins = new List<Plugin>();
                foreach (string pluginPath in Directory.GetDirectories(Path.Combine(ProjectPath, "Plugins")))
                {
                    // Check it's a valid plugin directory, there might be empty directories lying around
                    if (PluginPaths.Instance.IsTargetDirectory(pluginPath))
                    {
                        Plugin plugin = new(pluginPath);
                        plugins.Add(plugin);
                    }
                }

                return plugins;
            }
        }

        /// <summary>Replaces descriptor state from disk; read and parse errors propagate to the caller.</summary>
        public void LoadDescriptor()
        {
            ProjectDescriptor = ProjectDescriptor.Load(UProjectPath);
        }

        public string GetStagedBuildWindowsPath(Engine engineContext)
        {
            return Path.Combine(StagedBuildsPath, engineContext.GetWindowsPlatformName());
        }

        public Package? GetStagedPackage(Engine engineContext)
        {
            string path = GetStagedBuildWindowsPath(engineContext);
            return PackagePaths.Instance.IsTargetDirectory(path) ? new Package(path) : null;
        }

        public string GetStagedPackageExecutablePath(Engine engineContext)
        {
            return Path.Combine(GetStagedBuildWindowsPath(engineContext), Name + ".exe");
        }

        /// <summary>
        /// Adds or updates one plugin dependency entry in the project descriptor.
        /// </summary>
        public void SetPluginEnabled(string pluginName, bool enabled)
        {
            // Plugin enablement is descriptor state; project plugin files are copied or removed by separate operations.
            ProjectDescriptor.SetPluginEnabled(pluginName, enabled);
            ProjectDescriptor.Save(UProjectPath);
        }

        /**
         * Consider the project blueprint-only if it has zero modules
         * Alternatively it should also be possible to check the absence of a Source folder
         */
        public bool IsBlueprintOnly => ProjectDescriptor?.Modules.Count == 0;

        /// <summary>Updates the project version in DefaultGame.ini, requiring its settings section to exist.</summary>
        public void SetProjectVersion(string version)
        {
            string defaultGameIniPath = Path.Combine(TargetPath, "Config", "DefaultGame.ini");
            UnrealConfig config = new(defaultGameIniPath);
            ConfigSection projectSettings = config.GetSection("/Script/EngineSettings.GeneralProjectSettings")
                ?? throw new InvalidOperationException("Could not find GeneralProjectSettings section to update project version.");
            projectSettings.SetValue("ProjectVersion", version);
            config.Save();
        }

        /// <summary>
        /// Returns non-empty text or throws with a caller-specific validation message.
        /// </summary>
        private static string RequireText(string value, string parameterName, string message)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException(message, parameterName)
                : value;
        }

    }
}
