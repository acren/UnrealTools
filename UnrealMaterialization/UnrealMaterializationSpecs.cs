using FileMaterialization;

namespace UnrealMaterialization;

/// <summary>
/// Defines the authored files and cache boundaries used when materializing Unreal projects and plugins.
/// </summary>
public static class UnrealMaterializationSpecs
{
    /// <summary>
    /// Creates the canonical authored project subset, optionally restricting the project plugin tree.
    /// </summary>
    public static FileMaterializationSpec CreateProject(
        string projectDescriptorPath,
        IReadOnlySet<string>? includedPluginNames = null,
        bool includePluginBuildOutputs = false)
    {
        string descriptorPath = RequireDescriptor(projectDescriptorPath, ".uproject", "project");
        string projectDirectory = Path.GetDirectoryName(descriptorPath)!;
        FileMaterializationSpec spec = new()
        {
            { Path.GetFileName(descriptorPath), true },
            { "Config" },
            { "Source" },
            { "Content" },
            { Path.GetFileNameWithoutExtension(descriptorPath) + ".png" }
        };

        if (includedPluginNames == null)
        {
            return spec;
        }

        // The selected Plugins subtree is synchronized while generated outputs remain owned by the destination workspace.
        spec.Sync("Plugins");
        AddProjectPluginEntries(projectDirectory, spec, includedPluginNames, includePluginBuildOutputs);
        return spec;
    }

    /// <summary>
    /// Returns the plugin names declared by descriptor-bearing directories beneath a project Plugins root.
    /// </summary>
    public static IReadOnlySet<string> GetProjectPluginNames(string projectDescriptorPath)
    {
        string projectDirectory = Path.GetDirectoryName(RequireDescriptor(projectDescriptorPath, ".uproject", "project"))!;
        return GetProjectPluginDirectories(Path.Combine(projectDirectory, "Plugins"))
            .Select(Path.GetFileName)
            .Where(pluginName => !string.IsNullOrWhiteSpace(pluginName))
            .Select(pluginName => pluginName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates the canonical authored plugin subset, optionally including distributable build outputs.
    /// </summary>
    public static FileMaterializationSpec CreatePlugin(string pluginDirectoryPath, bool includeBuildOutputs = false)
    {
        if (pluginDirectoryPath == null)
        {
            throw new ArgumentNullException(nameof(pluginDirectoryPath));
        }

        string pluginDirectory = Path.GetFullPath(pluginDirectoryPath);
        string descriptorPath = FindRequiredPluginDescriptor(pluginDirectory);
        FileMaterializationSpec spec = new()
        {
            { Path.GetFileName(descriptorPath), true },
            { "Source" },
            { "Resources" },
            { "Content" },
            { "Config" },
            { "Extras" }
        };

        if (includeBuildOutputs)
        {
            spec.Add("Binaries");
            spec.Add("Build");
        }

        return spec;
    }

    /// <summary>
    /// Adds selected plugin payloads and preserves generated outputs that source materialization does not own.
    /// </summary>
    private static void AddProjectPluginEntries(
        string projectDirectory,
        FileMaterializationSpec spec,
        IReadOnlySet<string> includedPluginNames,
        bool includePluginBuildOutputs)
    {
        string pluginsPath = Path.Combine(projectDirectory, "Plugins");
        if (!Directory.Exists(pluginsPath) || includedPluginNames.Count == 0)
        {
            return;
        }

        // Files directly beneath Plugins remain part of the selected project payload.
        foreach (string pluginsRootFilePath in Directory.GetFiles(pluginsPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            spec.Add(Path.Combine("Plugins", Path.GetFileName(pluginsRootFilePath)));
        }

        foreach (string pluginDirectoryPath in GetProjectPluginDirectories(pluginsPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string pluginName = Path.GetFileName(pluginDirectoryPath);
            if (!includedPluginNames.Contains(pluginName))
            {
                continue;
            }

            string relativePluginPath = Path.Combine("Plugins", Path.GetRelativePath(pluginsPath, pluginDirectoryPath));
            spec.AddSubtree(relativePluginPath, CreatePlugin(pluginDirectoryPath, includePluginBuildOutputs));
            spec.Preserve(Path.Combine(relativePluginPath, "Intermediate"));
            if (!includePluginBuildOutputs)
            {
                spec.Preserve(Path.Combine(relativePluginPath, "Binaries"));
                spec.Preserve(Path.Combine(relativePluginPath, "Build"));
            }
        }
    }

    /// <summary>
    /// Enumerates descriptor-bearing plugin directories recursively so grouping folders are supported.
    /// </summary>
    private static IEnumerable<string> GetProjectPluginDirectories(string pluginsPath)
    {
        if (!Directory.Exists(pluginsPath))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.GetDirectories(pluginsPath, "*", SearchOption.AllDirectories)
            .Where(directory => Directory.GetFiles(directory, "*.uplugin", SearchOption.TopDirectoryOnly).Length == 1);
    }

    /// <summary>
    /// Resolves exactly one plugin descriptor at the plugin root so malformed directories fail visibly.
    /// </summary>
    private static string FindRequiredPluginDescriptor(string pluginDirectoryPath)
    {
        string[] descriptors = Directory.Exists(pluginDirectoryPath)
            ? Directory.GetFiles(pluginDirectoryPath, "*.uplugin", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
        if (descriptors.Length != 1)
        {
            throw new InvalidDataException($"Plugin directory must contain exactly one .uplugin descriptor: {pluginDirectoryPath}");
        }

        return descriptors[0];
    }

    /// <summary>
    /// Normalizes and validates a caller-supplied descriptor before deriving its materialization root.
    /// </summary>
    private static string RequireDescriptor(string descriptorPath, string extension, string kind)
    {
        if (descriptorPath == null)
        {
            throw new ArgumentNullException(nameof(descriptorPath));
        }

        string resolvedPath = Path.GetFullPath(descriptorPath);
        if (!string.Equals(Path.GetExtension(resolvedPath), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Required Unreal {kind} descriptor does not exist: {resolvedPath}", resolvedPath);
        }

        return resolvedPath;
    }
}
