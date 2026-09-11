using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Semver;

namespace UnrealUtilities
{
    public class PluginDescriptor
    {
        public string VersionName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public bool IsBetaVersion { get; set; }
        public string EngineVersionString { get; set; } = string.Empty;

        public List<ModuleDeclaration> Modules { get; set; } = new();

        public SemVersion SemVersion => SemVersion.Parse(VersionName, SemVersionStyles.Strict);
        public EngineVersion? EngineVersion => string.IsNullOrEmpty(EngineVersionString) ? null : new(EngineVersionString);

        /// <summary>Reads descriptor state, propagating file and parse failures to the caller.</summary>
        public static PluginDescriptor Load(string uPluginPath)
        {
            return JsonConvert.DeserializeObject<PluginDescriptor>(File.ReadAllText(uPluginPath))
                ?? throw new InvalidOperationException($"Could not deserialize plugin descriptor '{uPluginPath}'.");
        }
    }
}
