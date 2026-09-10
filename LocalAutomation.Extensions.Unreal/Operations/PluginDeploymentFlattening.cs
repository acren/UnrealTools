using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using LocalAutomation.Core.IO;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using LocalAutomation.Extensions.Unreal.Unreal;
using Microsoft.Extensions.Logging;
using UnrealAutomationCommon.Unreal;
using UnrealPluginFlattening;

namespace LocalAutomation.Extensions.Unreal.Operations
{
    /// <summary>
    /// Builds a staged single-plugin payload by embedding configured sibling plugins as renamed modules.
    /// </summary>
    internal static class PluginDeploymentFlattening
    {
        // Merge options accept several delimiters so values remain readable in a single property-grid text box.
        private static readonly char[] MergeEntryDelimiters = { ',', ';', '\r', '\n' };

        // Explicit prefixes are validated while parsing so invalid options fail before workspace materialization.
        private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        /// <summary>
        /// Applies merge rules to one staged plugin and synchronizes the persistent BuildPlugin package input.
        /// Merge-source plugins are refreshed into the isolated workspace before rewriting so every copied module comes
        /// from the same workspace project shape used by downstream validation.
        /// </summary>
        public static IReadOnlySet<string> StagePluginForDeployment(
            Targets.Plugin stagingPlugin,
            Project sourceProject,
            Project workspaceProject,
            string packageInputPluginPath,
            PluginDeployOptions deployOptions,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            _ = stagingPlugin ?? throw new ArgumentNullException(nameof(stagingPlugin));
            _ = sourceProject ?? throw new ArgumentNullException(nameof(sourceProject));
            _ = workspaceProject ?? throw new ArgumentNullException(nameof(workspaceProject));
            _ = deployOptions ?? throw new ArgumentNullException(nameof(deployOptions));
            _ = logger ?? throw new ArgumentNullException(nameof(logger));

            IReadOnlyList<MergeRule> mergeRules = ParseMergeRules(deployOptions.MergePlugins);
            IReadOnlyList<MergePlugin> mergePlugins = mergeRules.Count == 0
                ? Array.Empty<MergePlugin>()
                : MaterializeMergePlugins(sourceProject, workspaceProject, stagingPlugin.Name, mergeRules, logger, cancellationToken);

            if (mergePlugins.Count > 0)
            {
                logger.LogInformation("Flattening {MergePluginCount} merge plugin(s) into staged plugin '{PluginName}'.", mergePlugins.Count, stagingPlugin.Name);
            }

            // The archive-edited descriptor is the host input; the package owns generated content and build-cache stability.
            PluginFlattener.Flatten(stagingPlugin.Model.UPluginPath, mergePlugins, packageInputPluginPath,
                message => logger.LogInformation("{Message}", message), cancellationToken);

            /* Source archives and project validation consume staging, while BuildPlugin consumes the persistent input.
               Mirror the generated source payload back so both consumers receive the same flattened tree. */
            FileUtils.MaterializeDirectory(packageInputPluginPath, stagingPlugin.Model.PluginPath,
                MaterializationSpecs.CreatePlugin(packageInputPluginPath), logger, cancellationToken, mirrorDirectories: true);
            stagingPlugin.LoadDescriptor();
            // Deployment exclusion sets use workspace plugin directory names, matching Plugin.Name.
            return mergePlugins.Select(plugin => Path.GetFileName(Path.GetDirectoryName(plugin.DescriptorPath)!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Copies merge-source plugins into the isolated workspace and returns their descriptor paths and prefix options.
        /// </summary>
        private static IReadOnlyList<MergePlugin> MaterializeMergePlugins(
            Project sourceProject,
            Project workspaceProject,
            string targetPluginName,
            IReadOnlyList<MergeRule> mergeRules,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // Workspace plugin copies give flattening one stable project-local source for both name and path specifiers.
            Directory.CreateDirectory(workspaceProject.PluginsPath);
            List<MergePlugin> mergePlugins = new();
            HashSet<string> materializedPluginNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (MergeRule mergeRule in mergeRules)
            {
                Plugin sourcePlugin = ResolveMergePlugin(sourceProject, mergeRule.SourcePluginSpecifier);
                if (sourcePlugin.Name.Equals(targetPluginName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Deploy plugin cannot merge the target plugin into itself: {sourcePlugin.Name}");
                }

                if (!materializedPluginNames.Add(sourcePlugin.Name))
                {
                    throw new InvalidOperationException($"Deploy plugin merge list contains duplicate plugin '{sourcePlugin.Name}'.");
                }

                string workspaceMergePluginPath = Path.Combine(workspaceProject.PluginsPath, sourcePlugin.Name);
                logger.LogInformation("Materializing merge plugin '{PluginName}' into workspace: {WorkspacePluginPath}", sourcePlugin.Name, workspaceMergePluginPath);
                FileUtils.DeleteDirectoryIfExists(workspaceMergePluginPath);
                FileUtils.MaterializeDirectory(sourcePlugin.PluginPath, workspaceMergePluginPath, MaterializationSpecs.CreatePlugin(sourcePlugin), logger, cancellationToken);

                Plugin workspacePlugin = new(workspaceMergePluginPath);
                mergePlugins.Add(new MergePlugin(workspacePlugin.UPluginPath, mergeRule.EmbeddedPrefixOverride));
            }

            return mergePlugins;
        }

        /// <summary>
        /// Parses the target-local merge option into structured source plugin specifiers and optional embedded prefixes.
        /// </summary>
        private static IReadOnlyList<MergeRule> ParseMergeRules(string mergePlugins)
        {
            if (string.IsNullOrWhiteSpace(mergePlugins))
            {
                return Array.Empty<MergeRule>();
            }

            // Empty entries are ignored so users can format the option value across multiple readable lines.
            List<MergeRule> rules = new();
            foreach (string rawEntry in mergePlugins.Split(MergeEntryDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = rawEntry.Split(new[] { '=' }, 2, StringSplitOptions.TrimEntries);
                string sourcePluginSpecifier = parts[0];
                string? embeddedPrefixOverride = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : null;
                if (string.IsNullOrWhiteSpace(sourcePluginSpecifier))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(embeddedPrefixOverride))
                {
                    ValidateIdentifier(embeddedPrefixOverride!, $"embedded prefix override for merge plugin '{sourcePluginSpecifier}'");
                }

                rules.Add(new MergeRule(sourcePluginSpecifier, embeddedPrefixOverride));
            }

            return rules;
        }

        /// <summary>
        /// Resolves one merge plugin by explicit path or by plugin directory name under the project Plugins tree.
        /// </summary>
        private static Plugin ResolveMergePlugin(Project project, string sourcePluginSpecifier)
        {
            string? pluginPath = ResolveMergePluginPath(project, sourcePluginSpecifier);
            if (pluginPath == null)
            {
                throw new DirectoryNotFoundException($"Could not resolve merge plugin '{sourcePluginSpecifier}' under '{project.PluginsPath}'.");
            }

            return new Plugin(pluginPath);
        }

        /// <summary>
        /// Finds a merge plugin path from explicit path candidates first, then by recursive plugin-name search.
        /// </summary>
        private static string? ResolveMergePluginPath(Project project, string sourcePluginSpecifier)
        {
            foreach (string candidatePath in GetExplicitPluginPathCandidates(project, sourcePluginSpecifier))
            {
                if (PluginPaths.Instance.IsTargetDirectory(candidatePath))
                {
                    return Path.GetFullPath(candidatePath);
                }
            }

            if (!Directory.Exists(project.PluginsPath))
            {
                return null;
            }

            // Name lookup is recursive so grouped project plugin folders are supported without making users type paths.
            string pluginLookupName = GetPluginLookupName(sourcePluginSpecifier);
            string[] matchingPluginPaths = Directory.GetDirectories(project.PluginsPath, "*", SearchOption.AllDirectories)
                .Where(PluginPaths.Instance.IsTargetDirectory)
                .Where(path => string.Equals(Path.GetFileName(path), pluginLookupName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingPluginPaths.Length > 1)
            {
                throw new InvalidOperationException($"Merge plugin '{sourcePluginSpecifier}' is ambiguous: {string.Join(", ", matchingPluginPaths)}");
            }

            return matchingPluginPaths.SingleOrDefault();
        }

        /// <summary>
        /// Converts a path-like merge specifier back to the plugin directory name used inside the workspace copy.
        /// </summary>
        private static string GetPluginLookupName(string sourcePluginSpecifier)
        {
            string trimmedSpecifier = Path.TrimEndingDirectorySeparator(sourcePluginSpecifier);
            string fileName = Path.GetFileName(trimmedSpecifier);
            return string.IsNullOrWhiteSpace(fileName) ? sourcePluginSpecifier : fileName;
        }

        /// <summary>
        /// Returns the explicit path interpretations that are useful before falling back to plugin-name lookup.
        /// </summary>
        private static IEnumerable<string> GetExplicitPluginPathCandidates(Project project, string sourcePluginSpecifier)
        {
            if (Path.IsPathRooted(sourcePluginSpecifier))
            {
                yield return sourcePluginSpecifier;
                yield break;
            }

            // Relative plugin paths are interpreted from the host project first because deploy options are target-local.
            yield return Path.Combine(project.ProjectPath, sourcePluginSpecifier);
            yield return Path.Combine(project.PluginsPath, sourcePluginSpecifier);
        }

        /// <summary>
        /// Rejects malformed explicit prefixes before merge-source plugins are materialized.
        /// </summary>
        private static void ValidateIdentifier(string value, string description)
        {
            if (!IdentifierRegex.IsMatch(value))
            {
                throw new InvalidOperationException($"Invalid {description}: '{value}'.");
            }
        }

        /// <summary>
        /// Describes one user-authored merge option entry.
        /// </summary>
        private sealed class MergeRule
        {
            public MergeRule(string sourcePluginSpecifier, string? embeddedPrefixOverride)
            {
                SourcePluginSpecifier = sourcePluginSpecifier;
                EmbeddedPrefixOverride = embeddedPrefixOverride;
            }

            // The user-provided plugin name or path that identifies the merge source.
            public string SourcePluginSpecifier { get; }

            // The optional destination prefix that overrides generated child-prefix-plus-host-suffix naming.
            public string? EmbeddedPrefixOverride { get; }
        }
    }
}
