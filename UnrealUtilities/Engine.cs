using System;
using System.IO;
using Newtonsoft.Json;
using Semver;
using SystemUtilities.IO;

namespace UnrealUtilities
{
    [JsonObject(MemberSerialization.OptIn)]
    public class Engine : IEngineInstanceProvider
    {
        /// <summary>Gets the installation directory used by engine path calculations.</summary>
        [JsonProperty]
        public string TargetPath { get; }

        [JsonProperty]
        public string Key { get; set; } = string.Empty;

        public bool IsSourceBuild { get; private set; }

        public string Name => $"{Version} {EngineType}";
        public string DisplayName => Name;

        /// <summary>Gets whether the installation contains an editor executable.</summary>
        public bool IsValid => EnginePaths.Instance.IsTargetDirectory(TargetPath);

        public string EngineType => IsSourceBuild ? "Source" : "Launcher";

        public string BaseEditorName => Version.MajorVersion >= 5 ? "UnrealEditor" : "UE4Editor";

        public EngineVersion Version => EngineVersion.Load(this.GetBuildVersionPath());

        public SemVersion SemVersion => Semver.SemVersion.Parse(Version.ToString(), SemVersionStyles.Any);

        public string PluginsPath => Path.Combine(TargetPath, "Engine", "Plugins");

        /// <summary>Describes an installation at the supplied directory.</summary>
        [JsonConstructor]
        public Engine(string targetPath)
        {
            TargetPath = targetPath;
            IsSourceBuild = File.Exists(Path.Combine(TargetPath, "Default.uprojectdirs"));
        }

        /// <summary>
        /// Returns whether this Unreal engine install can build or launch with the requested configuration.
        /// </summary>
        public bool SupportsConfiguration(BuildConfiguration configuration)
        {
            if (configuration == BuildConfiguration.Debug
                || configuration == BuildConfiguration.Test)
            // Only support Debug and Test in source builds
            {
                return IsSourceBuild;
            }

            // Always support DebugGame, Development, Shipping
            return true;
        }

        public bool SupportsTestReports => Version >= new EngineVersion(4, 25);

        public string GetWindowsPlatformName()
        {
            if (Version.MajorVersion >= 5)
            {
                return "Windows";
            }

            return "WindowsNoEditor";
        }

        public Plugin? FindInstalledPlugin(string pluginName)
        {
            string plugins = Path.Combine(TargetPath, "Engine", "Plugins");
            string extension = "*.uplugin";
            string[] upluginPaths = Directory.GetFiles(plugins, extension, SearchOption.AllDirectories);
            foreach (string upluginPath in upluginPaths)
            {
                if (Path.GetFileNameWithoutExtension(upluginPath).Equals(pluginName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return new Plugin(Path.GetDirectoryName(upluginPath)!);
                }
            }

            return null;
        }

        /// <summary>Removes an installed plugin's files from the engine installation.</summary>
        public void UninstallPlugin(string pluginName)
        {
            Plugin plugin = FindInstalledPlugin(pluginName)
                ?? throw new InvalidOperationException("Could not find plugin in installed plugins");
            /* Uninstallation deletes plugin files. A plugin installed via Epic Launcher may remain registered there
               because this filesystem operation does not change the launcher's installation records. */
            FileUtils.DeleteDirectory(plugin.PluginPath);
        }

        public bool IsPluginInstalled(string pluginName)
        {
            return FindInstalledPlugin(pluginName) != null;
        }

        public Engine EngineInstance => this;
    }
}
