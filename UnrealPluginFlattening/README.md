# Unreal plugin flattening

A .NET 6 library for embedding source-only Unreal plugin dependencies into one generated plugin. Rewrites Unreal module and supported reflected type names, merges config, and synchronizes output without changing inputs.

## Use

Reference the `UnrealPluginFlattening` project from a .NET 6 or newer project:

```csharp
using UnrealPluginFlattening;

PluginFlattener.Flatten(
    "Plugins/MyPlugin/MyPlugin.uplugin",
    new[] { new MergePlugin("Plugins/Utility/Utility.uplugin") },
    "Intermediate/GeneratedPlugins/MyPlugin",
    Console.WriteLine);
```

Unreal module `Utility` becomes `UtilityMyPlugin`; set `MergePlugin.EmbeddedPrefix` to override the prefix. Supply every dependency to embed; others remain plugin references.

## Constraints

- Embedded dependencies cannot contain Content assets.
- Dependency Unreal module names must start with the plugin name; reflected type names must use it after `U`, `A`, `F`, or `E`.
- Output must not overlap any input plugin directory. After failure or cancellation, do not build or publish incomplete output.
- Not a general C++ parser: arbitrary namespaces and ABI symbols are not renamed. Verify dependencies can coexist, especially global state and external symbols.

## Build

```text
dotnet build UnrealPluginFlattening/UnrealPluginFlattening.csproj -c Release
```
