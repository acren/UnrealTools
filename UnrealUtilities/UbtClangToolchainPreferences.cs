using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Semver;

namespace UnrealUtilities
{
    public static class UbtClangToolchainPreferences
    {
        // Fixed search roots cover the common single-install path and two side-by-side install parent folders.
        private static readonly string[] FixedLlvmRootPaths =
        {
            @"C:\Program Files\LLVM",
            @"C:\LLVM",
            @"C:\Tools\LLVM"
        };

        // Preferred ranges preserve UBT's authored priority order plus SemVer bounds for matching.
        private readonly record struct PreferredClangRange(
            string Display,
            SemVersion MinVersion,
            SemVersion MaxVersion)
        {
            // UBT accepts a bare major version as a compiler-family selector, such as 16 for Clang 16.x.
            public string CompilerVersion => MinVersion.Major.ToString(CultureInfo.InvariantCulture);

            // Match installed compiler versions against this engine-authored preferred range.
            public bool Contains(SemVersion version)
            {
                return SemVersion.CompareSortOrder(version, MinVersion) >= 0
                    && SemVersion.CompareSortOrder(version, MaxVersion) <= 0;
            }
        }

        // Installed toolchains keep only the LLVM root and file version needed for selection and diagnostics.
        private readonly record struct InstalledClangToolchain(
            string ToolchainRoot,
            SemVersion Version);

        // Newer engines keep Windows toolchain versions in data-driven SDK metadata that UBT reads at runtime.
        private static string GetWindowsSdkJsonPath(Engine engine)
        {
            return Path.Combine(engine.TargetPath,
                "Engine",
                "Config",
                "Windows",
                "Windows_SDK.json");
        }

        // Older engines keep Windows toolchain versions in UBT source, so this remains the fallback source.
        private static string GetVersionsMetadataPath(Engine engine)
        {
            return Path.Combine(engine.TargetPath,
                "Engine",
                "Source",
                "Programs",
                "UnrealBuildTool",
                "Platform",
                "Windows",
                "MicrosoftPlatformSDK.Versions.cs");
        }

        // Resolve the first engine-preferred Clang family and LLVM root that are installed on the local machine.
        public static string? TryGetPreferredToolchain(Engine engine, out string compilerVersion, out string toolchainRoot)
        {
            compilerVersion = string.Empty;
            toolchainRoot = string.Empty;
            string? preferredRangesError = TryReadPreferredRanges(engine, out List<PreferredClangRange> preferredRanges);
            if (preferredRangesError != null)
            {
                return preferredRangesError;
            }

            List<InstalledClangToolchain> installedCandidates = DiscoverInstalledClangCandidates();
            foreach (PreferredClangRange preferredRange in preferredRanges)
            {
                List<InstalledClangToolchain> matchingCandidates = installedCandidates
                    .Where(candidate => preferredRange.Contains(candidate.Version))
                    .OrderByDescending(candidate => candidate.Version.Major)
                    .ThenByDescending(candidate => candidate.Version.Minor)
                    .ThenByDescending(candidate => candidate.Version.Patch)
                    .ToList();

                if (matchingCandidates.Count > 0)
                {
                    InstalledClangToolchain clangToolchain = matchingCandidates[0];

                    compilerVersion = preferredRange.CompilerVersion;
                    toolchainRoot = clangToolchain.ToolchainRoot;
                    return null;
                }
            }

            return BuildInstalledClangError(engine, preferredRanges, installedCandidates);
        }

        // Prefer the data-driven SDK metadata used by newer engines, then fall back to older UBT source metadata.
        private static string? TryReadPreferredRanges(Engine engine, out List<PreferredClangRange> preferredRanges)
        {
            string jsonMetadataPath = GetWindowsSdkJsonPath(engine);
            if (File.Exists(jsonMetadataPath))
            {
                return TryParsePreferredRangesFromJson(engine, jsonMetadataPath, out preferredRanges);
            }

            string sourceMetadataPath = GetVersionsMetadataPath(engine);
            if (!File.Exists(sourceMetadataPath))
            {
                preferredRanges = new List<PreferredClangRange>();
                return BuildResolutionError(engine, sourceMetadataPath, "The metadata file is missing.");
            }

            string metadata;
            try
            {
                // Read the UBT source file as text so the selected engine remains the source of truth.
                metadata = File.ReadAllText(sourceMetadataPath);
            }
            catch (Exception exception)
            {
                preferredRanges = new List<PreferredClangRange>();
                return BuildResolutionError(engine, sourceMetadataPath, $"The metadata file could not be read: {exception.Message}");
            }

            return TryParsePreferredRangesFromSource(engine, sourceMetadataPath, metadata, out preferredRanges);
        }

        // Parse the Windows SDK JSON format used by newer UBT versions, including UBT's comment-bearing JSON files.
        private static string? TryParsePreferredRangesFromJson(
            Engine engine,
            string metadataPath,
            out List<PreferredClangRange> preferredRanges)
        {
            preferredRanges = new List<PreferredClangRange>();
            string metadata;
            try
            {
                metadata = File.ReadAllText(metadataPath);
            }
            catch (Exception exception)
            {
                return BuildResolutionError(engine, metadataPath, $"The metadata file could not be read: {exception.Message}");
            }

            JsonDocumentOptions jsonOptions = new()
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };
            try
            {
                using JsonDocument document = JsonDocument.Parse(metadata, jsonOptions);
                if (!document.RootElement.TryGetProperty("PreferredClangVersions", out JsonElement preferredClangVersions)
                    || preferredClangVersions.ValueKind != JsonValueKind.Array)
                {
                    return BuildResolutionError(engine, metadataPath, "The PreferredClangVersions array could not be parsed.");
                }

                foreach (JsonElement preferredClangVersion in preferredClangVersions.EnumerateArray())
                {
                    if (preferredClangVersion.ValueKind != JsonValueKind.String)
                    {
                        return BuildResolutionError(engine, metadataPath, "A PreferredClangVersions entry was not a version range string.");
                    }

                    string? rangeText = preferredClangVersion.GetString();
                    if (string.IsNullOrWhiteSpace(rangeText))
                    {
                        return BuildResolutionError(engine, metadataPath, "A PreferredClangVersions entry was blank.");
                    }

                    string[] versionBounds = rangeText.Split('-', 2, StringSplitOptions.TrimEntries);
                    if (versionBounds.Length != 2)
                    {
                        return BuildResolutionError(engine, metadataPath, $"The preferred Clang range '{rangeText}' could not be parsed.");
                    }

                    string? rangeError = AddPreferredRange(engine, metadataPath, versionBounds[0], versionBounds[1], preferredRanges);
                    if (rangeError != null)
                    {
                        return rangeError;
                    }
                }
            }
            catch (JsonException exception)
            {
                return BuildResolutionError(engine, metadataPath, $"The metadata file could not be parsed as JSON: {exception.Message}");
            }

            if (preferredRanges.Count == 0)
            {
                return BuildResolutionError(engine, metadataPath, "No parseable PreferredClangVersions range was found.");
            }

            return null;
        }

        // Parse only the PreferredClangVersions initializer so unrelated UBT version ranges cannot be selected.
        private static string? TryParsePreferredRangesFromSource(
            Engine engine,
            string metadataPath,
            string metadata,
            out List<PreferredClangRange> preferredRanges)
        {
            preferredRanges = new List<PreferredClangRange>();
            Match preferredRangesBlock = Regex.Match(metadata,
                @"PreferredClangVersions\s*=\s*\{(?<Ranges>.*?)\};",
                RegexOptions.Singleline);
            if (!preferredRangesBlock.Success)
            {
                return BuildResolutionError(engine, metadataPath, "The PreferredClangVersions block could not be parsed.");
            }

            MatchCollection preferredRangeMatches = Regex.Matches(preferredRangesBlock.Groups["Ranges"].Value,
                @"VersionNumberRange\.Parse\(\s*""(?<Min>\d+(?:\.\d+)*)""\s*,\s*""(?<Max>\d+(?:\.\d+)*)""\s*\)");
            foreach (Match preferredRangeMatch in preferredRangeMatches)
            {
                string minText = preferredRangeMatch.Groups["Min"].Value;
                string maxText = preferredRangeMatch.Groups["Max"].Value;
                string? rangeError = AddPreferredRange(engine, metadataPath, minText, maxText, preferredRanges);
                if (rangeError != null)
                {
                    return rangeError;
                }
            }

            if (preferredRanges.Count == 0)
            {
                return BuildResolutionError(engine, metadataPath, "No parseable PreferredClangVersions range was found.");
            }

            return null;
        }

        // Convert one UBT-authored preferred range into comparable numeric bounds while preserving display order.
        private static string? AddPreferredRange(
            Engine engine,
            string metadataPath,
            string minText,
            string maxText,
            List<PreferredClangRange> preferredRanges)
        {
            if (!TryParseVersionBound(minText, out SemVersion minVersion)
                || !TryParseVersionBound(maxText, out SemVersion maxVersion))
            {
                return BuildResolutionError(engine, metadataPath, $"The preferred Clang range '{minText}' to '{maxText}' could not be parsed.");
            }

            preferredRanges.Add(new PreferredClangRange($"{minText} to {maxText}",
                minVersion,
                maxVersion));
            return null;
        }

        // Normalize UBT's shortened version bounds into SemVer for consistent comparison.
        private static bool TryParseVersionBound(string versionText, out SemVersion version)
        {
            version = new SemVersion(0, 0, 0);
            string[] components = versionText.Split('.');
            if (components.Length == 0 || components.Length > 3)
            {
                return false;
            }

            string normalizedVersion = string.Join(".", components.Concat(Enumerable.Repeat("0", 3 - components.Length)));
            try
            {
                version = SemVersion.Parse(normalizedVersion, SemVersionStyles.Any);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Gather local Clang candidates from the fixed LLVM locations and their side-by-side child installs.
        private static List<InstalledClangToolchain> DiscoverInstalledClangCandidates()
        {
            List<InstalledClangToolchain> candidates = new();
            HashSet<string> seenCompilerPaths = new(StringComparer.OrdinalIgnoreCase);
            foreach (string toolchainRoot in EnumerateClangToolchainRoots())
            {
                AddClangCandidate(toolchainRoot, candidates, seenCompilerPaths);
            }

            return candidates;
        }

        // Scan each fixed path as both a direct LLVM root and a parent containing versioned LLVM roots.
        private static IEnumerable<string> EnumerateClangToolchainRoots()
        {
            foreach (string fixedRootPath in FixedLlvmRootPaths)
            {
                foreach (string toolchainRoot in EnumerateLlvmRootAndChildren(fixedRootPath))
                {
                    yield return toolchainRoot;
                }
            }
        }

        // Include the parent itself for ordinary installs, plus direct child folders for versioned side-by-side installs.
        private static IEnumerable<string> EnumerateLlvmRootAndChildren(string rootPath)
        {
            yield return rootPath;
            if (!Directory.Exists(rootPath))
            {
                yield break;
            }

            string[] childDirectories;
            try
            {
                childDirectories = Directory.GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                yield break;
            }

            foreach (string childDirectory in childDirectories)
            {
                yield return childDirectory;
            }
        }

        // Add a candidate only when a clang-cl executable exists and exposes a usable file version.
        private static void AddClangCandidate(
            string toolchainRoot,
            List<InstalledClangToolchain> candidates,
            HashSet<string> seenCompilerPaths)
        {
            string compilerPath = Path.Combine(toolchainRoot, "bin", "clang-cl.exe");
            if (!File.Exists(compilerPath))
            {
                return;
            }

            string fullCompilerPath = Path.GetFullPath(compilerPath);
            if (!seenCompilerPaths.Add(fullCompilerPath))
            {
                return;
            }

            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(fullCompilerPath);
            if (versionInfo.FileMajorPart <= 0)
            {
                return;
            }

            int minor = Math.Max(versionInfo.FileMinorPart, 0);
            int patch = Math.Max(versionInfo.FileBuildPart, 0);
            SemVersion version = new(versionInfo.FileMajorPart, minor, patch);
            candidates.Add(new InstalledClangToolchain(Path.GetFullPath(toolchainRoot), version));
        }

        // Requirement failures name both the engine and file so the missing source of truth is actionable.
        private static string BuildResolutionError(Engine engine, string metadataPath, string detail)
        {
            return $"Could not resolve an engine-preferred Clang compiler version for '{engine.DisplayName}' from '{metadataPath}'. {detail}";
        }

        // Requirement failures list both sides of the match so users can see which fixed LLVM folders were scanned.
        private static string BuildInstalledClangError(
            Engine engine,
            List<PreferredClangRange> preferredRanges,
            List<InstalledClangToolchain> installedCandidates)
        {
            string preferredRangeList = string.Join(", ", preferredRanges.Select(range => range.Display));
            string installedCandidateList = installedCandidates.Count == 0
                ? "none"
                : string.Join("; ", installedCandidates.Select(candidate => $"{candidate.Version} at {candidate.ToolchainRoot}"));
            string searchedRootList = string.Join(", ", FixedLlvmRootPaths);

            return $"No installed Clang matches '{engine.DisplayName}' preferences. Required: {preferredRangeList}. Found: {installedCandidateList}. Searched: {searchedRootList}.";
        }
    }
}
