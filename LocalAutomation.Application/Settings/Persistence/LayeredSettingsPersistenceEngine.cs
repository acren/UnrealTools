using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalAutomation.Core;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Persistence;
using LocalAutomation.Runtime;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LocalAutomation.Application;

/// <summary>
/// Executes generic persisted settings mechanics over caller-supplied ordered layer definitions and descriptors.
/// </summary>
internal sealed class LayeredSettingsPersistenceEngine
{
    // Value converters are supplied by the application systems so token conversion stays independent of descriptor discovery policy.
    private readonly IReadOnlyList<ISettingValueConverter> _valueConverters;

    /// <summary>
    /// Represents one materialized persisted settings layer in low-to-high precedence order.
    /// </summary>
    private readonly record struct LoadedLayer
    {
        /// <summary>
        /// Creates one loaded stack entry for a persisted settings layer.
        /// </summary>
        public LoadedLayer(LayerDefinition definition, PersistedSettingValueCollection values)
        {
            Definition = definition;
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <summary>
        /// Gets the definition that produced this loaded layer.
        /// </summary>
        public LayerDefinition Definition { get; }

        /// <summary>
        /// Gets the loaded values for this layer.
        /// </summary>
        public PersistedSettingValueCollection Values { get; }
    }

    /// <summary>
    /// Defines one concrete persisted settings file layer in an ordered operation stack.
    /// </summary>
    public readonly record struct LayerDefinition
    {
        /// <summary>
        /// Creates one layer definition in the persisted layer precedence list.
        /// </summary>
        public LayerDefinition(PersistenceScope scope, string name, string filePath)
        {
            Scope = scope;
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Layer name must be provided.", nameof(name)) : name;
            FilePath = string.IsNullOrWhiteSpace(filePath) ? throw new ArgumentException("Layer file path must be provided.", nameof(filePath)) : filePath;
        }

        /// <summary>
        /// Gets the persistence scope represented by this definition.
        /// </summary>
        public PersistenceScope Scope { get; }

        /// <summary>
        /// Gets the stable diagnostic name used for telemetry tags after the layer is loaded.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the concrete persisted settings file path for this materialized layer.
        /// </summary>
        public string FilePath { get; }
    }

    /// <summary>
    /// Creates the persistence engine from generic value conversion dependencies.
    /// </summary>
    public LayeredSettingsPersistenceEngine(IReadOnlyList<ISettingValueConverter> valueConverters)
    {
        _valueConverters = valueConverters?.ToList() ?? throw new ArgumentNullException(nameof(valueConverters));
    }

    /// <summary>
    /// Resolves the effective persisted values available across the caller-supplied ordered layer stack.
    /// </summary>
    public IReadOnlyDictionary<string, JToken> ResolveEffectiveValues(IReadOnlyList<LayerDefinition> layerDefinitions)
    {
        if (layerDefinitions == null)
        {
            throw new ArgumentNullException(nameof(layerDefinitions));
        }

        using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("ResolveEffectiveValues");
        IReadOnlyList<LoadedLayer> layers = LoadLayers(layerDefinitions);
        Dictionary<string, JToken> values = ResolveEffectiveValues(layers);
        foreach (LoadedLayer layer in layers)
        {
            activity.SetTag(layer.Definition.Name + "_value.count", layer.Values.Values.Count);
        }

        activity.SetTag("resolved_value.count", values.Count);
        return values;
    }

    /// <summary>
    /// Applies already-resolved persisted values to one reflected settings owner through caller-supplied descriptors.
    /// </summary>
    public void ApplyValues(object owner, IReadOnlyList<PersistedSettingDescriptor> descriptors, IReadOnlyDictionary<string, JToken> resolvedValues)
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        if (descriptors == null)
        {
            throw new ArgumentNullException(nameof(descriptors));
        }

        if (resolvedValues == null)
        {
            throw new ArgumentNullException(nameof(resolvedValues));
        }

        ApplyResolvedValues(owner, descriptors, resolvedValues);
    }

    /// <summary>
    /// Captures one owner as sparse writes and removals using caller-supplied descriptors and default tokens.
    /// </summary>
    public void CaptureValues(
        PersistedSettingsWriteBatch batch,
        object owner,
        IReadOnlyList<PersistedSettingDescriptor> descriptors,
        IReadOnlyDictionary<string, JToken> defaultValues,
        IReadOnlyList<LayerDefinition> layerDefinitions)
    {
        if (batch == null)
        {
            throw new ArgumentNullException(nameof(batch));
        }

        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        if (descriptors == null)
        {
            throw new ArgumentNullException(nameof(descriptors));
        }

        if (defaultValues == null)
        {
            throw new ArgumentNullException(nameof(defaultValues));
        }

        if (layerDefinitions == null)
        {
            throw new ArgumentNullException(nameof(layerDefinitions));
        }

        CaptureSparseOwnerValues(batch, owner, descriptors, defaultValues, LoadLayers(layerDefinitions));
    }

    /// <summary>
    /// Captures persisted tokens for one reflected owner through caller-supplied descriptors.
    /// </summary>
    public Dictionary<string, JToken> CaptureValueTokens(object owner, IReadOnlyList<PersistedSettingDescriptor> descriptors)
    {
        Dictionary<string, JToken> values = new(StringComparer.Ordinal);
        AddMissingValueTokens(owner, descriptors, values);
        return values;
    }

    /// <summary>
    /// Adds missing persisted tokens to an existing map without replacing keys the caller already captured.
    /// </summary>
    public void AddMissingValueTokens(object owner, IReadOnlyList<PersistedSettingDescriptor> descriptors, IDictionary<string, JToken> values)
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        if (descriptors == null)
        {
            throw new ArgumentNullException(nameof(descriptors));
        }

        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        foreach (PersistedSettingDescriptor descriptor in descriptors)
        {
            if (!values.ContainsKey(descriptor.Key) && TryReadValueToken(owner, descriptor, out JToken? token))
            {
                values[descriptor.Key] = token!.DeepClone();
            }
        }
    }

    /// <summary>
    /// Loads caller-materialized persisted layer definitions into stack entries from lowest to highest priority.
    /// </summary>
    private static IReadOnlyList<LoadedLayer> LoadLayers(IReadOnlyList<LayerDefinition> layerDefinitions)
    {
        List<LoadedLayer> layers = new();
        foreach (LayerDefinition definition in layerDefinitions)
        {
            layers.Add(LoadLayer(definition));
        }

        return layers;
    }

    /// <summary>
    /// Loads one persisted layer definition and packages its scope, diagnostic name, and values into a stack entry.
    /// </summary>
    private static LoadedLayer LoadLayer(LayerDefinition definition)
    {
        return new LoadedLayer(definition, LoadCollection(definition.FilePath, definition.Name));
    }

    /// <summary>
    /// Applies a detached batch of persisted setting writes to disk by loading the current files and patching only the
    /// provided keys.
    /// </summary>
    public void SaveCapturedSettings(PersistedSettingsWriteBatch batch)
    {
        if (batch == null)
        {
            throw new ArgumentNullException(nameof(batch));
        }

        IEnumerable<string> affectedFilePaths = batch.FileRemovals.Keys
            .Concat(batch.FileWrites.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in affectedFilePaths)
        {
            PersistedSettingValueCollection existingValues = LoadCollection(filePath);
            if (batch.FileRemovals.TryGetValue(filePath, out HashSet<string>? fileRemovals))
            {
                foreach (string key in fileRemovals)
                {
                    existingValues.Values.Remove(key);
                }
            }

            if (batch.FileWrites.TryGetValue(filePath, out PersistedSettingValueCollection? fileWrites))
            {
                foreach ((string key, JToken value) in fileWrites.Values)
                {
                    existingValues.Values[key] = value.DeepClone();
                }
            }

            SaveCollection(filePath, existingValues);
        }
    }

    /// <summary>
    /// Captures one settings owner as sparse write and removal operations for the owner's configured write scopes.
    /// </summary>
    private void CaptureSparseOwnerValues(
        PersistedSettingsWriteBatch batch,
        object owner,
        IReadOnlyList<PersistedSettingDescriptor> descriptors,
        IReadOnlyDictionary<string, JToken> defaultValues,
        IReadOnlyList<LoadedLayer> layers)
    {
        foreach (PersistedSettingDescriptor descriptor in descriptors)
        {
            if (!TryReadValueToken(owner, descriptor, out JToken? token))
            {
                continue;
            }

            int writeLayerIndex = FindLayerIndex(layers, descriptor.WriteScope);
            if (writeLayerIndex < 0)
            {
                throw new InvalidOperationException($"Persisted setting '{descriptor.Key}' cannot be written because scope '{descriptor.WriteScope}' is not available in the current layer stack.");
            }

            LoadedLayer writeLayer = layers[writeLayerIndex];

            if (TryResolveInheritedToken(descriptor, defaultValues, layers, writeLayerIndex, out JToken? inheritedToken)
                && JToken.DeepEquals(token, inheritedToken))
            {
                batch.RemoveValue(writeLayer.Definition.FilePath, descriptor.Key);
                continue;
            }

            batch.WriteValue(writeLayer.Definition.FilePath, descriptor.Key, token!);
        }
    }

    /// <summary>
    /// Resolves the value a layer inherits from lower stack entries before its own write is applied.
    /// </summary>
    private static bool TryResolveInheritedToken(
        PersistedSettingDescriptor descriptor,
        IReadOnlyDictionary<string, JToken> defaultValues,
        IReadOnlyList<LoadedLayer> layers,
        int writeLayerIndex,
        out JToken? inheritedToken)
    {
        for (int layerIndex = writeLayerIndex - 1; layerIndex >= 0; layerIndex--)
        {
            if (layers[layerIndex].Values.Values.TryGetValue(descriptor.Key, out inheritedToken))
            {
                return true;
            }
        }

        return defaultValues.TryGetValue(descriptor.Key, out inheritedToken);
    }

    /// <summary>
    /// Returns the index of the first stack entry for the requested scope, or -1 when that layer is unavailable.
    /// </summary>
    private static int FindLayerIndex(IReadOnlyList<LoadedLayer> layers, PersistenceScope scope)
    {
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            if (layers[layerIndex].Definition.Scope == scope)
            {
                return layerIndex;
            }
        }

        return -1;
    }

    /// <summary>
    /// Merges loaded stack entries into one effective value map where later stack entries override earlier entries.
    /// </summary>
    private static Dictionary<string, JToken> ResolveEffectiveValues(IReadOnlyList<LoadedLayer> layers)
    {
        Dictionary<string, JToken> values = new(StringComparer.Ordinal);
        foreach (LoadedLayer layer in layers)
        {
            OverlayValues(values, layer.Values);
        }

        return values;
    }

    /// <summary>
    /// Overlays one persisted value collection onto the effective result map.
    /// </summary>
    private static void OverlayValues(IDictionary<string, JToken> destination, PersistedSettingValueCollection source)
    {
        foreach ((string key, JToken value) in source.Values)
        {
            destination[key] = value.DeepClone();
        }
    }

    /// <summary>
    /// Applies already-resolved values to one reflected settings owner.
    /// </summary>
    private void ApplyResolvedValues(object owner, IReadOnlyList<PersistedSettingDescriptor> descriptors, IReadOnlyDictionary<string, JToken> resolvedValues)
    {
        foreach (PersistedSettingDescriptor descriptor in descriptors)
        {
            if (!resolvedValues.TryGetValue(descriptor.Key, out JToken? token))
            {
                continue;
            }

            if (!TryDeserializeValue(descriptor.ValueType, token, out object? restoredValue))
            {
                continue;
            }

            if (descriptor.Property.CanWrite)
            {
                descriptor.Property.SetValue(owner, restoredValue);
                continue;
            }

            object? propertyValue = descriptor.Property.GetValue(owner);
            ApplyToExistingCollection(propertyValue, restoredValue);
        }
    }

    /// <summary>
    /// Loads one persisted value collection from disk while recording a tracing span for the storage layer involved.
    /// </summary>
    private static PersistedSettingValueCollection LoadCollection(string filePath, string layerName = "")
    {
        using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("LoadSettingsFile");
        bool fileExists = File.Exists(filePath);
        activity.SetTag("layer.name", layerName)
            .SetTag("file.exists", fileExists);

        if (fileExists)
        {
            try
            {
                activity.SetTag("file.length", new FileInfo(filePath).Length);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        JsonFileStateStore<PersistedSettingValueCollection> store = new(
            filePath: filePath,
            createDefaultState: static () => new PersistedSettingValueCollection(),
            createSerializer: static () => new JsonSerializer());

        PersistedSettingValueCollection state = store.Load().State;
        activity.SetTag("value.count", state.Values.Count);
        return state;
    }

    /// <summary>
    /// Saves one persisted value collection to disk.
    /// </summary>
    private static void SaveCollection(string filePath, PersistedSettingValueCollection state)
    {
        if (state.Values.Count == 0)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                ApplicationLogger.Logger.LogInformation("Deleted empty settings file '{SettingsFilePath}'.", filePath);
            }

            return;
        }

        JsonFileStateStore<PersistedSettingValueCollection> store = new(
            filePath: filePath,
            createDefaultState: static () => new PersistedSettingValueCollection(),
            createSerializer: static () => new JsonSerializer());

        store.Save(state);
        // Log after the write completes so settings diagnostics describe files that were actually saved successfully.
        ApplicationLogger.Logger.LogInformation("Saved settings file '{SettingsFilePath}' with {SettingCount} value(s).", filePath, state.Values.Count);
    }

    /// <summary>
    /// Reads and serializes one reflected property value.
    /// </summary>
    private bool TryReadValueToken(object owner, PersistedSettingDescriptor descriptor, out JToken? token)
    {
        object? value = descriptor.Property.GetValue(owner);
        return TrySerializeValue(descriptor.ValueType, value, out token);
    }

    /// <summary>
    /// Converts one runtime value into a persisted token using registered converters when available.
    /// </summary>
    private bool TrySerializeValue(Type valueType, object? value, out JToken? token)
    {
        ISettingValueConverter? converter = _valueConverters.FirstOrDefault(item => item.CanConvert(valueType));
        object? serializableValue = converter != null ? converter.Serialize(valueType, value) : SerializeSimpleValue(valueType, value);
        if (serializableValue == null && value != null)
        {
            token = null;
            return false;
        }

        token = JToken.FromObject(serializableValue ?? JValue.CreateNull());
        return true;
    }

    /// <summary>
    /// Restores one runtime value from a persisted token using registered converters when available.
    /// </summary>
    private bool TryDeserializeValue(Type valueType, JToken token, out object? value)
    {
        ISettingValueConverter? converter = _valueConverters.FirstOrDefault(item => item.CanConvert(valueType));
        if (converter != null)
        {
            value = converter.Deserialize(valueType, token);
            return true;
        }

        return TryDeserializeSimpleValue(valueType, token, out value);
    }

    /// <summary>
    /// Serializes primitive and collection values without requiring custom converters.
    /// </summary>
    private static object? SerializeSimpleValue(Type valueType, object? value)
    {
        if (value == null)
        {
            return null;
        }

        Type nonNullableType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (nonNullableType.IsEnum)
        {
            return value.ToString();
        }

        if (nonNullableType == typeof(string) || nonNullableType == typeof(bool) || nonNullableType.IsPrimitive || nonNullableType == typeof(decimal))
        {
            return value;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            List<object?> items = new();
            foreach (object? item in enumerable)
            {
                items.Add(item);
            }

            return items;
        }

        return null;
    }

    /// <summary>
    /// Restores primitive and collection values without requiring custom converters.
    /// </summary>
    private static bool TryDeserializeSimpleValue(Type valueType, JToken token, out object? value)
    {
        Type nonNullableType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (token.Type == JTokenType.Null)
        {
            value = null;
            return true;
        }

        if (nonNullableType.IsEnum)
        {
            value = Enum.Parse(nonNullableType, token.Value<string>()!, ignoreCase: true);
            return true;
        }

        if (nonNullableType == typeof(string))
        {
            value = token.Value<string>() ?? string.Empty;
            return true;
        }

        if (nonNullableType == typeof(bool))
        {
            value = token.Value<bool>();
            return true;
        }

        if (nonNullableType.IsPrimitive || nonNullableType == typeof(decimal))
        {
            value = token.ToObject(nonNullableType, JsonSerializer.CreateDefault());
            return true;
        }

        if (typeof(IList).IsAssignableFrom(nonNullableType))
        {
            value = token.ToObject(nonNullableType, JsonSerializer.CreateDefault());
            return value != null;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Applies restored items into an existing collection-backed property.
    /// </summary>
    private static void ApplyToExistingCollection(object? existingValue, object? restoredValue)
    {
        if (existingValue is not IList existingList || restoredValue is not IEnumerable restoredEnumerable)
        {
            return;
        }

        existingList.Clear();
        foreach (object? item in restoredEnumerable)
        {
            existingList.Add(item);
        }
    }

}
