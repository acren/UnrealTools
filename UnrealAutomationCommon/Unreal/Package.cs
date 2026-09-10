using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace UnrealAutomationCommon.Unreal
{
    /// <summary>Describes a packaged executable and its Unreal filesystem layout.</summary>
    public class Package : IEngineInstanceProvider
    {
        /// <summary>Gets the directory containing the packaged executable.</summary>
        public string TargetPath { get; }

        /// <summary>Loads package identity from a directory containing an executable.</summary>
        [JsonConstructor]
        public Package(string targetPath)
        {
            if (!PackagePaths.Instance.IsTargetDirectory(targetPath))
            {
                throw new ArgumentException($"Package '{targetPath}' does not contain an executable.", nameof(targetPath));
            }

            TargetPath = targetPath;
            Name = Path.GetFileNameWithoutExtension(ExecutablePath);
        }

        public string ExecutablePath => PackagePaths.Instance.FindRequiredTargetFile(TargetPath);

        /// <summary>Gets the source project directory when this is a staged project build.</summary>
        public string? HostProjectPath
        {
            get
            {
                string projectPath = Path.GetFullPath(Path.Combine(TargetPath, @"..\..\..\")); // Up 3 levels
                if (ProjectPaths.Instance.IsTargetDirectory(projectPath))
                {
                    return projectPath;
                }

                return null;
            }
        }

        /// <summary>Reads a standalone source project model when the package has a staged-build parent.</summary>
        public Project? HostProject => HostProjectPath is string path ? new Project(path) : null;

        public string LogsPath => Path.Combine(TargetPath, Name, "Saved", "Logs");

        private EngineVersion EngineVersion => new(FileVersionInfo.GetVersionInfo(ExecutablePath));

        public Engine EngineInstance => EngineFinder.GetRequiredEngineInstall(EngineVersion);

        public string EngineInstanceName => EngineInstance.DisplayName;

        public string Name { get; }

        public bool IsValid => PackagePaths.Instance.IsTargetFile(ExecutablePath);
    }
}
