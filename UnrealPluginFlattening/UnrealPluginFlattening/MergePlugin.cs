namespace UnrealPluginFlattening;

/// <summary>A source dependency to embed, identified by its descriptor rather than project discovery rules.</summary>
/// <param name="DescriptorPath">Path to the dependency's .uplugin file.</param>
/// <param name="EmbeddedPrefix">Explicit Unreal module/type prefix, or null to derive it from the host name.</param>
public sealed record MergePlugin(string DescriptorPath, string? EmbeddedPrefix = null);
