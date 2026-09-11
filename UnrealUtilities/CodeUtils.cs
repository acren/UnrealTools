using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnrealUtilities
{
    /// <summary>Generates Unreal module source files and descriptor entries from embedded templates.</summary>
    public static class CodeUtils
    {
        /// <summary>Adds an Unreal module to the project using its own copyright settings.</summary>
        public static void AddCodeModule(this Project project, string moduleName)
        {
            AddCodeModule(project.UProjectPath, project.SourcePath, project, moduleName);
        }

        /// <summary>Adds an Unreal module to the plugin using copyright settings from the supplied project.</summary>
        public static void AddCodeModule(this Plugin plugin, string moduleName, Project project)
        {
            AddCodeModule(plugin.UPluginPath, Path.Combine(plugin.PluginPath, "Source"), project, moduleName);
        }

        /// <summary>Creates source files before publishing their Unreal module entry in the descriptor.</summary>
        private static void AddCodeModule(string descriptorPath, string sourcePath, Project project, string moduleName)
        {
            JObject fileContent = JObject.Parse(File.ReadAllText(descriptorPath));
            if (!fileContent.ContainsKey("Modules"))
            {
                // Content-only descriptors have no module collection until their first source module is added.
                fileContent["Modules"] = new JArray();
            }

            // Edit the JSON field directly so descriptor properties outside the model survive generation.
            JArray modules = fileContent["Modules"] as JArray ?? throw new Exception("Modules property must be an array");
            JObject newModule = new JObject
            {
                { "Name", moduleName },
                { "Type", "Runtime" },
                { "LoadingPhase", "Default" }
            };
            modules.Add(newModule);

            // Each Unreal module owns a source directory containing its build rules and public/private source files.
            string sourceModuleDir = Path.Combine(sourcePath, moduleName);

            Dictionary<string, string> values = new()
            {
                { "COPYRIGHT_LINE", $"// {project.GetCopyrightNotice()}" },
                { "MODULE_NAME", moduleName }
            };

            // Resolve every resource before mutation so missing templates cannot publish incomplete modules.
            Dictionary<string, string> renderedFiles = new()
            {
                [Path.Combine(sourceModuleDir, $"{moduleName}.build.cs")] = RenderTemplate("module.build.cs", values),
                [Path.Combine(sourceModuleDir, "Public", $"{moduleName}.h")] = RenderTemplate("module.h", values),
                [Path.Combine(sourceModuleDir, "Private", $"{moduleName}.cpp")] = RenderTemplate("module.cpp", values)
            };
            foreach (var renderedFile in renderedFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(renderedFile.Key)!);
                File.WriteAllText(renderedFile.Key, renderedFile.Value);
            }

            // Publish only after all source writes succeed; unrelated descriptor fields remain intact.
            using StreamWriter file = File.CreateText(descriptorPath);
            using JsonTextWriter writer = new(file);
            writer.Formatting = Formatting.Indented;
            writer.Indentation = 1;
            writer.IndentChar = '\u0009';
            fileContent.WriteTo(writer);
        }

        /// <summary>Renders a required assembly template independently of the caller's working directory.</summary>
        private static string RenderTemplate(string templateName, Dictionary<string, string> values)
        {
            string resourceName = $"UnrealUtilities.templates.{templateName}.template";
            using Stream templateStream = typeof(CodeUtils).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Required Unreal module template is missing: {resourceName}");
            using StreamReader reader = new(templateStream);
            string templateContent = reader.ReadToEnd();
            foreach (var entry in values)
            {
                templateContent = templateContent.Replace($"%{entry.Key}%", entry.Value);
            }
            return templateContent;
        }
    }
}
