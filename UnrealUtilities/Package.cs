using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace UnrealUtilities
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

        /// <summary>Supplies the engine's automation report template when the package does not already contain it.</summary>
        public void PrepareAutomationReportTemplate(Engine engine)
        {
            // Packaged automation expects the engine-relative template even when packaging omitted that content.
            string relativePath = Path.Combine("Engine", "Content", "Automation", "Report-Template.html");
            string destination = Path.Combine(TargetPath, relativePath);
            if (File.Exists(destination))
            {
                return;
            }

            string source = Path.Combine(engine.TargetPath, relativePath);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Expected engine report template.", source);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }

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
