using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;

namespace UnrealAutomationCommon.Unreal
{
    /// <summary>Owns plugin descriptor state, version editing, and model-level engine resolution.</summary>
    public class Plugin : IEngineInstanceProvider
    {
        // A resolved project shares its descriptor snapshot with model navigation until explicitly reloaded.
        private Project? _hostProject;

        /// <summary>Gets the directory containing the plugin descriptor.</summary>
        public string TargetPath { get; }

        /// <summary>Reads the descriptor from a plugin directory.</summary>
        [JsonConstructor]
        public Plugin(string targetPath)
        {
            if (!PluginPaths.Instance.IsTargetDirectory(targetPath))
            {
                throw new ArgumentException($"Plugin '{targetPath}' does not contain a .uplugin.", nameof(targetPath));
            }

            TargetPath = targetPath;

            LoadDescriptor();
        }

        public string UPluginPath => PluginPaths.Instance.FindRequiredTargetFile(TargetPath);

        /// <summary>
        /// Reuses the resolved host project descriptor snapshot for model-level engine resolution.
        /// </summary>
        public Project HostProject => _hostProject ??= new Project(HostProjectPath);

        public string Name => new DirectoryInfo(TargetPath).Name;

        public bool IsValid => PluginPaths.Instance.IsTargetDirectory(TargetPath) && PluginDescriptor != null;

        /// <summary>Gets the descriptor snapshot from construction or the last explicit reload.</summary>
        public PluginDescriptor PluginDescriptor { get; private set; } = null!;

        /**
         * Consider the plugin blueprint-only if it has zero modules
         * Alternatively it should also be possible to check the absence of a Source folder
         */
        public bool IsBlueprintOnly => PluginDescriptor?.Modules?.Count == 0;

        /**
         * Check if the plugin has runtime modules (modules that will be included in packaged builds)
         * Runtime modules are those that are not editor-only types
         */
        public bool HasRuntimeModules => PluginDescriptor?.Modules?.Any(m => m.Type != "Editor" &&
                                                                             m.Type != "EditorNoCommandlet" &&
                                                                             m.Type != "EditorAndProgram" &&
                                                                             m.Type != "UncookedOnly") == true;

        public string PluginPath => TargetPath;

        public string HostProjectPath => string.IsNullOrEmpty(PluginPath) ? "" : Path.GetFullPath(Path.Combine(PluginPath, @"..\..\")); // Up 2 levels

        public Engine EngineInstance
        {
            get
            {
                // If plugin descriptor has an engine version, find engine install using that
                EngineVersion? descriptorVersion = PluginDescriptor?.EngineVersion;
                if (descriptorVersion != null)
                {
                    return EngineFinder.GetRequiredEngineInstall(descriptorVersion);
                }

                // Use host project version
                return HostProject.EngineInstance;
            }
        }

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

        /// <summary>Replaces descriptor state from disk; read and parse errors propagate to the caller.</summary>
        public void LoadDescriptor()
        {
            PluginDescriptor = PluginDescriptor.Load(UPluginPath);
        }

        /// <summary>Writes the integer version derived from the semantic version, returning whether it changed.</summary>
        public bool UpdateVersionInteger()
        {
            SemVersion version = PluginDescriptor.SemVersion;
            int versionInt = version.ToInt();
            JObject descriptorJObject = JObject.Parse(File.ReadAllText(UPluginPath));
            string versionKey = "Version";
            JToken? versionToken = descriptorJObject[versionKey];
            if (versionToken != null && versionToken.ToObject<int>() == versionInt)
            {
                // Already correct, did not update
                return false;
            }
            descriptorJObject[versionKey] = versionInt;

            using FileStream fs = File.Create(UPluginPath);
            using StreamWriter sw = new(fs);
            using JsonTextWriter jtw = new(sw)
            {
                Formatting = Formatting.Indented,
                Indentation = 1,
                IndentChar = '\t'
            };
            (new JsonSerializer()).Serialize(jtw, descriptorJObject);

            return true;
        }

    }
}
