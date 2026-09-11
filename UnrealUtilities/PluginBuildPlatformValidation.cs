using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnrealUtilities
{
    public static class PluginBuildPlatformValidation
    {
        // Validate the effective requested plugin target platforms against the selected engine.
        public static string? CheckRequirementsSatisfied(Engine engine, IReadOnlyCollection<string> requestedPlatforms)
        {
            if (requestedPlatforms.Count == 0)
            {
                return null;
            }

            List<string> unavailablePlatforms = requestedPlatforms.Where(platform => !IsTargetPlatformAvailable(engine, platform)).ToList();
            if (unavailablePlatforms.Count == 0)
            {
                return null;
            }

            string unavailablePlatformList = string.Join(", ", unavailablePlatforms);
            if (engine.IsSourceBuild)
            {
                return $"{unavailablePlatformList} is not available in '{engine.DisplayName}'. Build or install support for that platform, or disable it before running BuildPlugin.";
            }

            return $"{unavailablePlatformList} is not installed for '{engine.DisplayName}'. Install platform support in Epic Games Launcher, or disable it before running BuildPlugin.";
        }

        // Resolve the final TargetPlatforms value after AdditionalArguments overrides so validation matches execution.
        public static List<string> GetRequestedTargetPlatforms(bool buildWin64, bool buildLinux, string additionalArguments)
        {
            Arguments arguments = new();
            arguments.SetKeyValue("TargetPlatforms", string.Join('+', GetSelectedTargetPlatforms(buildWin64, buildLinux)));
            if (!string.IsNullOrWhiteSpace(additionalArguments))
            {
                arguments.AddRawArgsString(additionalArguments);
            }
            return GetRequestedTargetPlatforms(arguments);
        }

        // Parse a built argument list into the final set of requested target platforms.
        public static List<string> GetRequestedTargetPlatforms(Arguments arguments)
        {
            Argument? targetPlatformsArgument = arguments.GetArgument("TargetPlatforms");
            if (targetPlatformsArgument == null || string.IsNullOrWhiteSpace(targetPlatformsArgument.Value))
            {
                return new List<string>();
            }

            return targetPlatformsArgument.Value
                .Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(platform => platform.Trim())
                .Where(platform => !string.IsNullOrWhiteSpace(platform))
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();
        }

        // Normalize supported platform selections into the explicit list passed to BuildPlugin.
        public static List<string> GetSelectedTargetPlatforms(bool buildWin64, bool buildLinux)
        {
            if (!buildWin64 && !buildLinux)
            {
                // If nothing is selected, specify Win64 only to avoid Win32 being compiled.
                buildWin64 = true;
            }

            List<string> selectedPlatforms = new();
            if (buildWin64)
            {
                selectedPlatforms.Add("Win64");
            }

            if (buildLinux)
            {
                selectedPlatforms.Add("Linux");
            }

            return selectedPlatforms;
        }

        /// <summary>Recognizes UAT's built-platform declaration so callers can retain only the latest reported set.</summary>
        public static bool TryParseBuiltTargetPlatforms(string line, out List<string> platforms)
        {
            const string prefix = "Building plugin for target platforms:";
            int prefixIndex = line.IndexOf(prefix, StringComparison.InvariantCultureIgnoreCase);
            platforms = new List<string>();
            if (prefixIndex < 0)
            {
                return false;
            }

            string value = line.Substring(prefixIndex + prefix.Length).Trim();
            platforms = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(platform => platform.Trim())
                .Where(platform => !string.IsNullOrWhiteSpace(platform))
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();
            return true;
        }

        /// <summary>Identifies requested platforms UAT omitted even when its process exited successfully.</summary>
        public static List<string> GetSkippedTargetPlatforms(IEnumerable<string> requestedPlatforms, IEnumerable<string> builtPlatforms)
        {
            return requestedPlatforms.Where(platform => !builtPlatforms.Contains(platform, StringComparer.InvariantCultureIgnoreCase)).ToList();
        }

        // Installed engines advertise code-platform support through required target files, so check those generically.
        private static bool IsTargetPlatformAvailable(Engine engine, string platform)
        {
            List<string> requiredFiles = GetRequiredTargetFiles(engine, platform);
            if (requiredFiles.Count == 0)
            {
                return true;
            }

            return requiredFiles.All(File.Exists);
        }

        // Mirror Unreal's installed-engine gate by checking the per-platform UnrealGame target files Unreal expects.
        private static List<string> GetRequiredTargetFiles(Engine engine, string platform)
        {
            if (engine.IsSourceBuild)
            {
                return new List<string>();
            }

            return new List<string>
            {
                Path.Combine(engine.TargetPath, "Engine", "Binaries", platform, "UnrealGame.target"),
                Path.Combine(engine.TargetPath, "Engine", "Binaries", platform, $"UnrealGame-{platform}-Shipping.target")
            };
        }
    }
}
