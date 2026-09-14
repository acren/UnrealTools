using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace UnrealPluginFlattening;

/// <summary>Produces a source-flattened plugin without changing its inputs or discarding generated build caches.</summary>
public static partial class PluginFlattener
{
    // These subtrees are authored payload; Binaries and Intermediate belong to the consumer's build system.
    private static readonly string[] PayloadDirectories = { "Source", "Config", "Content", "Resources", "Extras" };
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cs", ".h", ".hh", ".hpp", ".inl", ".ini", ".md", ".txt", ".usf", ".ush"
    };
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex PascalTokens = new("[A-Z]+(?=[A-Z][a-z]|$)|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant);
    private static readonly Regex ReflectedClass = new(@"U(?:CLASS|INTERFACE)\s*\([^)]*\)\s*(?:class|struct)\s+(?:[A-Za-z_][A-Za-z0-9_]*_API\s+)?(?<Name>[UA][A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Singleline);
    private static readonly Regex ReflectedStruct = new(@"USTRUCT\s*\([^)]*\)\s*struct\s+(?:[A-Za-z_][A-Za-z0-9_]*_API\s+)?(?<Name>F[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Singleline);
    private static readonly Regex ReflectedEnum = new(@"UENUM\s*\([^)]*\)\s*enum\s+(?:class\s+)?(?<Name>E[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Singleline);
    private static readonly Regex TestFixture = new(@"\bTEST_CLASS(?:_WITH_FLAGS|_WITH_BASE|_WITH_BASE_AND_FLAGS)?\s*\(\s*(?<Name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Embeds the specified source dependencies into a separate generated plugin. The caller supplies the complete merge
    /// set; remaining plugin dependencies are retained. Unsupported assets and name collisions fail before output writes.
    /// Unchanged payload files retain timestamps, stale generated payload is removed, and build directories are untouched.
    /// </summary>
    public static void Flatten(string hostPluginDescriptor, IReadOnlyList<MergePlugin> mergePlugins,
        string outputDirectory, Action<string> log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mergePlugins);
        ArgumentNullException.ThrowIfNull(log);
        cancellationToken.ThrowIfCancellationRequested();
        Plugin host = ReadPlugin(hostPluginDescriptor, null);
        Plugin[] dependencies = mergePlugins.Select(input => ReadPlugin(input.DescriptorPath, input.EmbeddedPrefix)).ToArray();
        string output = Path.GetFullPath(outputDirectory);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) { host.Name };
        foreach (Plugin input in dependencies.Prepend(host))
        {
            // Disallow either overlap direction so stale-output removal cannot reach authored files.
            if (IsWithin(output, input.Directory) || IsWithin(input.Directory, output))
                throw new InvalidOperationException("Generated plugin output must not overlap any source plugin directory.");
        }
        foreach (Plugin dependency in dependencies)
        {
            if (!names.Add(dependency.Name))
                throw new InvalidOperationException($"Duplicate or self merge dependency '{dependency.Name}'.");
            string content = Path.Combine(dependency.Directory, "Content");
            if (Directory.Exists(content) && Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException($"Merge plugin '{dependency.Name}' contains Content assets; flattening would change their Unreal package paths.");
        }

        // Compute a complete, collision-checked output plan before synchronizing the persistent build input.
        List<ModuleRename> modules = BuildModuleRenames(host, dependencies);
        List<ReflectedRename> reflected = FindReflectedRenames(modules, cancellationToken);
        Dictionary<string, string> replacements = BuildReplacements(modules, reflected);
        Dictionary<string, string> fixtures = FindFixtureRenames(modules, cancellationToken);
        Func<string, string> rewrite = MakeRewriter(replacements, false);
        Func<string, string> rewriteFixtures = MakeRewriter(fixtures, true);
        Dictionary<string, GeneratedFile> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in PayloadDirectories)
        {
            AddDirectory(files, Path.Combine(host.Directory, directory), directory, rewrite,
                rewriteFixtures, directory is "Source" or "Config", cancellationToken);
        }
        foreach (ModuleRename module in modules)
        {
            log($"Embedding Unreal module '{module.SourceName}' as '{module.EmbeddedName}'.");
            AddDirectory(files, module.SourceDirectory, Path.Combine("Source", module.EmbeddedName),
                rewrite, rewriteFixtures, true, cancellationToken);
        }

        JObject descriptor = MergeDescriptor(host, dependencies, modules);
        AddFile(files, host.Name + ".uplugin", new GeneratedFile(null, descriptor.ToString()));
        MergeConfig(files, host.Name, dependencies, reflected, rewrite, cancellationToken);
        foreach (ModuleRename module in modules)
        {
            string buildFile = Path.Combine("Source", module.EmbeddedName, module.EmbeddedName + ".Build.cs");
            if (!files.ContainsKey(buildFile))
                throw new FileNotFoundException($"Embedded Unreal module build rule is missing: {buildFile}");
        }
        Synchronize(output, files, log, cancellationToken);
    }

    // Descriptor parsing is confined to this boundary; source identity comes from the descriptor filename.
    private static Plugin ReadPlugin(string descriptorPath, string? prefix)
    {
        string path = Path.GetFullPath(descriptorPath);
        if (!string.Equals(Path.GetExtension(path), ".uplugin", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Expected a .uplugin descriptor: {path}");
        string name = Path.GetFileNameWithoutExtension(path);
        ValidateIdentifier(name);
        if (prefix != null) ValidateIdentifier(prefix);
        return new Plugin(name, Path.GetDirectoryName(path)!, prefix, JObject.Parse(File.ReadAllText(path)));
    }

    // Resolve every Unreal module name once, including explicit host declarations, before generating source paths.
    private static List<ModuleRename> BuildModuleRenames(Plugin host, IEnumerable<Plugin> dependencies)
    {
        HashSet<string> claimed = Declarations(host.Descriptor, "Modules").Select(RequiredName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<ModuleRename> result = new();
        foreach (Plugin dependency in dependencies)
        {
            string prefix = dependency.Prefix ?? DerivePrefix(dependency.Name, host.Name);
            JObject[] declarations = Declarations(dependency.Descriptor, "Modules").ToArray();
            if (declarations.Length == 0)
                throw new InvalidOperationException($"Merge plugin '{dependency.Name}' has no source Unreal modules.");
            foreach (JObject declaration in declarations)
            {
                string source = RequiredName(declaration);
                if (!source.StartsWith(dependency.Name, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Unreal module '{source}' must start with its plugin name '{dependency.Name}'.");
                string embedded = prefix + source[dependency.Name.Length..];
                ValidateIdentifier(embedded);
                if (!claimed.Add(embedded)) throw new InvalidOperationException($"Embedded Unreal module name collision: {embedded}");
                string directory = Path.Combine(dependency.Directory, "Source", source);
                if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Missing Unreal module source: {directory}");
                result.Add(new ModuleRename(dependency.Name, source, embedded, prefix, directory, declaration));
            }
        }
        return result;
    }

    // Shared leading Pascal-case tokens are not repeated in the generated host suffix.
    private static string DerivePrefix(string source, string host)
    {
        string[] sourceTokens = PascalTokens.Matches(source).Select(match => match.Value).ToArray();
        string[] hostTokens = PascalTokens.Matches(host).Select(match => match.Value).ToArray();
        if (sourceTokens.Length == 0) sourceTokens = new[] { source };
        if (hostTokens.Length == 0) hostTokens = new[] { host };
        int shared = 0;
        while (shared < sourceTokens.Length && shared < hostTokens.Length && sourceTokens[shared] == hostTokens[shared]) shared++;
        string suffix = string.Concat(hostTokens.Skip(shared));
        if (suffix.Length == 0) throw new InvalidOperationException($"An explicit embedded prefix is required for '{source}' in '{host}'.");
        return source + suffix;
    }

    // Reflected names need both C++ renaming and redirects for serialized /Script references.
    private static List<ReflectedRename> FindReflectedRenames(IEnumerable<ModuleRename> modules, CancellationToken cancellationToken)
    {
        List<ReflectedRename> result = new();
        foreach (ModuleRename module in modules)
        foreach (string path in Directory.EnumerateFiles(module.SourceDirectory, "*", SearchOption.AllDirectories).Where(IsText))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text = File.ReadAllText(path);
            foreach ((string kind, Regex pattern) in new[] { ("Class", ReflectedClass), ("Struct", ReflectedStruct), ("Enum", ReflectedEnum) })
            foreach (Match match in pattern.Matches(text))
            {
                string source = match.Groups["Name"].Value;
                if (!source[1..].StartsWith(module.PluginName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Reflected type '{source}' in '{path}' must use its plugin prefix '{module.PluginName}'.");
                string embedded = source[0] + module.Prefix + source[(module.PluginName.Length + 1)..];
                result.Add(new ReflectedRename(kind, module.SourceName, module.EmbeddedName, source, embedded));
            }
        }
        return result;
    }

    // Module identifiers occur inside filenames and API macros, so replacement is deliberately not word-boundary limited.
    private static Dictionary<string, string> BuildReplacements(IEnumerable<ModuleRename> modules, IEnumerable<ReflectedRename> reflected)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (ModuleRename module in modules)
        {
            AddReplacement(result, module.SourceName, module.EmbeddedName);
            AddReplacement(result, module.SourceName.ToUpperInvariant() + "_API", module.EmbeddedName.ToUpperInvariant() + "_API");
        }
        foreach (ReflectedRename type in reflected) AddReplacement(result, type.SourceName, type.EmbeddedName);
        return result;
    }

    // CQTest registers the fixture identifier globally, independently of its Unreal module and display directory.
    private static Dictionary<string, string> FindFixtureRenames(IEnumerable<ModuleRename> modules, CancellationToken cancellationToken)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (ModuleRename module in modules)
        foreach (string path in Directory.EnumerateFiles(module.SourceDirectory, "*", SearchOption.AllDirectories).Where(IsText))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (Match match in TestFixture.Matches(File.ReadAllText(path)))
            {
                string name = match.Groups["Name"].Value;
                AddReplacement(result, name, module.Prefix + name);
            }
        }
        return result;
    }

    // Contradictory mappings cannot be resolved by ordering without silently binding a reference to the wrong dependency.
    private static void AddReplacement(Dictionary<string, string> replacements, string source, string embedded)
    {
        if (replacements.TryGetValue(source, out string? existing) && existing != embedded)
            throw new InvalidOperationException($"Conflicting replacement for '{source}': '{existing}' and '{embedded}'.");
        replacements[source] = embedded;
    }

    // A single regex pass prevents generated names from being recursively expanded by shorter source prefixes.
    private static Func<string, string> MakeRewriter(Dictionary<string, string> replacements, bool wholeIdentifiers)
    {
        if (replacements.Count == 0) return value => value;
        string Pattern(KeyValuePair<string, string> pair)
        {
            string source = Regex.Escape(pair.Key);
            if (wholeIdentifiers) return @"\b" + source + @"\b";
            string suffix = pair.Value.StartsWith(pair.Key, StringComparison.Ordinal) ? pair.Value[pair.Key.Length..] : "";
            return source + (suffix.Length == 0 ? "" : "(?!" + Regex.Escape(suffix) + ")");
        }
        Regex regex = new(string.Join("|", replacements.OrderByDescending(pair => pair.Key.Length).Select(Pattern)), RegexOptions.CultureInvariant);
        return value => regex.Replace(value, match => replacements[match.Value]);
    }

    // Plan text transforms in memory; binary payload remains file-backed instead of loading entire Content trees into RAM.
    private static void AddDirectory(Dictionary<string, GeneratedFile> files, string source, string destination,
        Func<string, string> rewrite, Func<string, string> rewriteFixtures, bool transform, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source)) return;
        foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, path);
            string output = Path.Combine(destination, transform ? rewrite(relative) : relative);
            string? text = transform && IsText(path) ? rewriteFixtures(rewrite(File.ReadAllText(path))) : null;
            AddFile(files, output, new GeneratedFile(path, text));
        }
    }

    // Case-insensitive output identities match Unreal's Windows build environment even on a case-sensitive authoring host.
    private static void AddFile(Dictionary<string, GeneratedFile> files, string path, GeneratedFile file)
    {
        if (!files.TryAdd(path, file)) throw new InvalidOperationException($"Generated plugin file collision: {path}");
    }

    // Descriptor changes stay at the serialization boundary; host policy wins for dependencies that remain external.
    private static JObject MergeDescriptor(Plugin host, IEnumerable<Plugin> dependencies, IEnumerable<ModuleRename> modules)
    {
        JObject descriptor = (JObject)host.Descriptor.DeepClone();
        JArray declarations = new();
        foreach (ModuleRename module in modules)
        {
            JObject declaration = (JObject)module.Declaration.DeepClone();
            declaration["Name"] = module.EmbeddedName;
            declarations.Add(declaration);
        }
        foreach (JObject declaration in Declarations(host.Descriptor, "Modules")) declarations.Add(declaration.DeepClone());
        descriptor["Modules"] = declarations;
        HashSet<string> mergedNames = dependencies.Select(plugin => plugin.Name).Append(host.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JObject> external = new(StringComparer.OrdinalIgnoreCase);
        foreach (Plugin plugin in dependencies.Prepend(host))
        foreach (JObject dependency in Declarations(plugin.Descriptor, "Plugins"))
        {
            string name = RequiredName(dependency);
            if (!mergedNames.Contains(name)) external.TryAdd(name, (JObject)dependency.DeepClone());
        }
        if (external.Count == 0) descriptor.Remove("Plugins");
        else descriptor["Plugins"] = new JArray(external.Values);
        return descriptor;
    }

    // Merge generated config from original inputs on every invocation so repeated builds never append duplicate sections.
    private static void MergeConfig(Dictionary<string, GeneratedFile> files, string hostName, IEnumerable<Plugin> dependencies,
        IEnumerable<ReflectedRename> reflected, Func<string, string> rewrite, CancellationToken cancellationToken)
    {
        string destination = Path.Combine("Config", $"Default{hostName}.ini");
        List<string> lines = files.TryGetValue(destination, out GeneratedFile? current)
            ? (current.Text ?? File.ReadAllText(current.Source!)).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList() : new();
        bool changed = false;
        foreach (Plugin plugin in dependencies)
        {
            string root = Path.Combine(plugin.Directory, "Config");
            if (!Directory.Exists(root)) continue;
            foreach (string path in Directory.EnumerateFiles(root, "*.ini", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                lines.Add("");
                lines.Add($"; Merged from {plugin.Name}/{Path.GetRelativePath(root, path).Replace('\\', '/')}");
                lines.AddRange(File.ReadAllLines(path).Select(rewrite));
                changed = true;
            }
        }
        string[] redirects = reflected.Select(type =>
        {
            string source = type.Kind == "Enum" ? type.SourceName : type.SourceName[1..];
            string embedded = type.Kind == "Enum" ? type.EmbeddedName : type.EmbeddedName[1..];
            return $"+{type.Kind}Redirects=(OldName=\"/Script/{type.SourceModule}.{source}\",NewName=\"/Script/{type.EmbeddedModule}.{embedded}\")";
        }).Distinct(StringComparer.Ordinal).ToArray();
        if (redirects.Length > 0)
        {
            int section = lines.FindIndex(line => line.Trim() == "[CoreRedirects]");
            if (section < 0) { lines.Add(""); lines.Add("[CoreRedirects]"); section = lines.Count - 1; }
            int insert = section + 1;
            while (insert < lines.Count && !lines[insert].TrimStart().StartsWith("[", StringComparison.Ordinal)) insert++;
            HashSet<string> existing = lines.ToHashSet(StringComparer.Ordinal);
            foreach (string redirect in redirects) if (existing.Add(redirect)) lines.Insert(insert++, redirect);
            changed = true;
        }
        if (changed) files[destination] = new GeneratedFile(null, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    // Reject malformed descriptor entries rather than silently producing a partially declared plugin.
    private static IEnumerable<JObject> Declarations(JObject descriptor, string key)
    {
        if (descriptor[key] == null) return Array.Empty<JObject>();
        if (descriptor[key] is not JArray array || array.Any(item => item is not JObject))
            throw new InvalidOperationException($"Plugin descriptor '{key}' must be an array of objects.");
        return array.Cast<JObject>();
    }

    // Missing names prevent reliable collision detection and must fail before writing generated output.
    private static string RequiredName(JObject declaration)
    {
        string? name = declaration.Value<string>("Name");
        return string.IsNullOrWhiteSpace(name) ? throw new InvalidOperationException("Missing Name in plugin descriptor declaration.") : name;
    }

    // Only known source/config extensions are rewritten; binary payload is copied byte-for-byte.
    private static bool IsText(string path) => TextExtensions.Contains(Path.GetExtension(path));

    // Unreal module names participate in filenames, macros, and C++ identifiers.
    private static void ValidateIdentifier(string value)
    {
        if (!Identifier.IsMatch(value)) throw new InvalidOperationException($"Invalid generated identifier: '{value}'.");
    }

    // Full-path boundaries avoid sibling-prefix matches such as Plugins/Foo and Plugins/FooBar.
    private static bool IsWithin(string path, string root) =>
        string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // Private records keep parsed metadata and generated file ownership local to one flattening operation.
    private sealed record Plugin(string Name, string Directory, string? Prefix, JObject Descriptor);
    private sealed record ModuleRename(string PluginName, string SourceName, string EmbeddedName, string Prefix, string SourceDirectory, JObject Declaration);
    private sealed record ReflectedRename(string Kind, string SourceModule, string EmbeddedModule, string SourceName, string EmbeddedName);
    private sealed record GeneratedFile(string? Source, string? Text);
}
