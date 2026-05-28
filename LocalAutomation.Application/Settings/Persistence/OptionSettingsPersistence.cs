using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LocalAutomation.Core;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Newtonsoft.Json.Linq;

namespace LocalAutomation.Application;

/// <summary>
/// Applies, captures, and saves target-scoped option-set owners through the shared persistence primitives.
/// </summary>
internal sealed class OptionSettingsPersistence
{
    // Descriptor lists are cached by owner type and generated owner prefix because reflected metadata is stable at runtime.
    private readonly Dictionary<(Type OwnerType, string Prefix), IReadOnlyList<PersistedSettingDescriptor>> _descriptorCache = new();
    // Option owner prefixes are derived from extension ownership and the option-set owner segment.
    private readonly Dictionary<Type, string> _optionOwnerPrefixCache = new();
    // The catalog supplies operation descriptors and extension ownership for option-generated key prefixes.
    private readonly ExtensionCatalog _catalog;
    // The engine reads supplied layers and captures sparse writes for supplied descriptors.
    private readonly LayeredSettingsPersistenceEngine _engine;
    // Layer definitions are materialized outside the engine so target file policy stays in application code.
    private readonly SettingsLayerDefinitionProvider _layerDefinitions;
    // Target discovery resolves runtime targets into registered ids and stable paths for target-scoped operations.
    private readonly TargetDiscoveryService _targets;
    // Detached batches are saved through the shared batch saver so callers can debounce writes safely.
    private readonly PersistedSettingsBatchSaver _batchSaver;

    /// <summary>
    /// Creates the option interaction boundary for the shared application persistence engine.
    /// </summary>
    internal OptionSettingsPersistence(
        LayeredSettingsPersistenceEngine engine,
        SettingsLayerDefinitionProvider layerDefinitions,
        ExtensionCatalog catalog,
        TargetDiscoveryService targets,
        PersistedSettingsBatchSaver batchSaver)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _layerDefinitions = layerDefinitions ?? throw new ArgumentNullException(nameof(layerDefinitions));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _batchSaver = batchSaver ?? throw new ArgumentNullException(nameof(batchSaver));
    }

    /// <summary>
    /// Applies effective target-scoped persisted values to the provided live option-set owners.
    /// </summary>
    public void ApplyOptionValues(IEnumerable<object> optionSets, IOperationTarget? target)
    {
        if (!TryCreateTargetLayers(target, out TargetTypeId targetTypeId, out IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> layers))
        {
            return;
        }

        List<object> optionSetList = optionSets?.ToList() ?? throw new ArgumentNullException(nameof(optionSets));
        using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("ApplyOptionValues")
            .SetTag("target.type", targetTypeId.Value)
            .SetTag("count", optionSetList.Count);

        IReadOnlyDictionary<string, JToken> resolvedValues = _engine.ResolveEffectiveValues(layers);
        int descriptorCount = 0;
        foreach (object optionSet in optionSetList)
        {
            IReadOnlyList<PersistedSettingDescriptor> descriptors = GetOptionSetDescriptors(optionSet.GetType());
            descriptorCount += descriptors.Count;
            _engine.ApplyValues(optionSet, descriptors, resolvedValues);
        }

        activity.SetTag("resolved_value.count", resolvedValues.Count)
            .SetTag("descriptor.count", descriptorCount);
    }

    /// <summary>
    /// Captures target-scoped option-set values into a detached sparse write batch.
    /// </summary>
    public PersistedSettingsWriteBatch CaptureOptionValues(IEnumerable<object> optionSets, IOperationTarget? target)
    {
        PersistedSettingsWriteBatch batch = new();
        if (!TryCreateTargetLayers(target, out TargetTypeId targetTypeId, out IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> layers))
        {
            return batch;
        }

        if (optionSets == null)
        {
            throw new ArgumentNullException(nameof(optionSets));
        }

        foreach (object optionSet in optionSets)
        {
            IReadOnlyList<PersistedSettingDescriptor> descriptors = GetOptionSetDescriptors(optionSet.GetType());
            object defaultOwner = CreateDefaultOwner(optionSet.GetType());
            Dictionary<string, JToken> defaultValues = _engine.CaptureValueTokens(defaultOwner, descriptors);
            _engine.CaptureValues(batch, optionSet, descriptors, defaultValues, layers);
        }

        return batch;
    }

    /// <summary>
    /// Captures and immediately saves target-scoped option-set values for callers that do not need deferred writes.
    /// </summary>
    public void SaveOptionValues(IEnumerable<object> optionSets, IOperationTarget? target)
    {
        if (target == null)
        {
            return;
        }

        _batchSaver.Save(CaptureOptionValues(optionSets, target));
    }

    /// <summary>
    /// Returns option descriptor sets for startup key validation without exposing option policy outside the option system.
    /// </summary>
    internal IEnumerable<(string OwnerLabel, IReadOnlyList<PersistedSettingDescriptor> Descriptors)> GetGeneratedKeyDescriptors()
    {
        foreach (Assembly assembly in GetKnownOptionSettingsAssemblies())
        {
            foreach (Type optionSetType in assembly.GetTypes().Where(type => typeof(OperationOptions).IsAssignableFrom(type) && !type.IsAbstract))
            {
                yield return (optionSetType.FullName ?? optionSetType.Name, GetOptionSetDescriptors(optionSetType));
            }
        }
    }

    /// <summary>
    /// Returns descriptors for one option-set owner using the option system's generated owner prefix.
    /// </summary>
    private IReadOnlyList<PersistedSettingDescriptor> GetOptionSetDescriptors(Type optionSetType)
    {
        string ownerPrefix = GetOptionSetOwnerPrefix(optionSetType);
        if (_descriptorCache.TryGetValue((optionSetType, ownerPrefix), out IReadOnlyList<PersistedSettingDescriptor>? cachedDescriptors))
        {
            return cachedDescriptors;
        }

        IReadOnlyList<PersistedSettingDescriptor> descriptors = PersistedSettingDescriptor.CreateForOwner(optionSetType, ownerPrefix);
        _descriptorCache[(optionSetType, ownerPrefix)] = descriptors;
        return descriptors;
    }

    /// <summary>
    /// Creates a default option-set owner so sparse captures can remove values that match owner defaults.
    /// </summary>
    private static object CreateDefaultOwner(Type ownerType)
    {
        return Activator.CreateInstance(ownerType)
            ?? throw new InvalidOperationException($"Settings owner '{ownerType.FullName}' must have a public parameterless constructor.");
    }

    /// <summary>
    /// Produces the stable generated-key owner prefix for an option-set type.
    /// </summary>
    private string GetOptionSetOwnerPrefix(Type optionSetType)
    {
        if (_optionOwnerPrefixCache.TryGetValue(optionSetType, out string? cachedPrefix))
        {
            return cachedPrefix;
        }

        string extensionId = ResolveOwningModuleId(optionSetType);
        string ownerSegment = PersistedSettingDescriptor.ResolveOwnerSegment(optionSetType);
        string prefix = string.IsNullOrWhiteSpace(extensionId)
            ? ownerSegment
            : extensionId + "." + ownerSegment;
        _optionOwnerPrefixCache[optionSetType] = prefix;
        return prefix;
    }

    /// <summary>
    /// Resolves the extension id that owns an option-set type when the catalog recorded one during module registration.
    /// </summary>
    private string ResolveOwningModuleId(Type ownerType)
    {
        string? ownedModuleId = _catalog.GetOwningModuleId(ownerType.Assembly);
        return string.IsNullOrWhiteSpace(ownedModuleId) ? string.Empty : ownedModuleId;
    }

    /// <summary>
    /// Returns assemblies that can contain option-set owners referenced by registered operations.
    /// </summary>
    private IEnumerable<Assembly> GetKnownOptionSettingsAssemblies()
    {
        HashSet<Assembly> assemblies = new();
        foreach (OperationDescriptor descriptor in _catalog.OperationDescriptors)
        {
            assemblies.Add(descriptor.OperationType.Assembly);
        }

        foreach (TargetDescriptor descriptor in _catalog.TargetDescriptors)
        {
            assemblies.Add(descriptor.TargetType.Assembly);
        }

        assemblies.Add(typeof(TargetSettings).Assembly);
        return assemblies;
    }

    /// <summary>
    /// Materializes target-scoped layers for a registered target while allowing null or unregistered targets to no-op.
    /// </summary>
    private bool TryCreateTargetLayers(
        IOperationTarget? target,
        out TargetTypeId targetTypeId,
        out IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> layers)
    {
        if (target == null)
        {
            targetTypeId = default;
            layers = Array.Empty<LayeredSettingsPersistenceEngine.LayerDefinition>();
            return false;
        }

        TargetTypeId? resolvedTargetTypeId = _targets.GetTargetTypeId(target);
        if (resolvedTargetTypeId == null)
        {
            targetTypeId = default;
            layers = Array.Empty<LayeredSettingsPersistenceEngine.LayerDefinition>();
            return false;
        }

        string targetPath = _targets.GetTargetPath(target);
        TargetKey targetKey = TargetKeyUtility.BuildTargetKey(resolvedTargetTypeId.Value, targetPath);
        targetTypeId = resolvedTargetTypeId.Value;
        layers = _layerDefinitions.CreateTargetLayers(targetPath, targetKey);
        return true;
    }
}
