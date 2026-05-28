using System;
using System.Collections.Generic;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Newtonsoft.Json.Linq;

namespace LocalAutomation.Application;

/// <summary>
/// Applies and saves target settings owners through the shared persistence primitives.
/// </summary>
internal sealed class TargetSettingsPersistence
{
    // Descriptor lists are cached by settings owner type and target type because target settings keys include both parts.
    private readonly Dictionary<(Type OwnerType, TargetTypeId TargetTypeId), IReadOnlyList<PersistedSettingDescriptor>> _descriptorCache = new();
    // Target owner prefixes are cached separately so repeated apply/save operations do not rebuild stable key prefixes.
    private readonly Dictionary<(Type OwnerType, TargetTypeId TargetTypeId), string> _targetOwnerPrefixCache = new();
    // The engine reads supplied layers and captures sparse writes for supplied descriptors.
    private readonly LayeredSettingsPersistenceEngine _engine;
    // Layer definitions are materialized from target identity before the generic engine sees the operation.
    private readonly SettingsLayerDefinitionProvider _layerDefinitions;
    // Target discovery resolves runtime targets into registered ids and stable paths.
    private readonly TargetDiscoveryService _targets;
    // Detached batches are saved through the shared batch saver so write behavior stays centralized.
    private readonly PersistedSettingsBatchSaver _batchSaver;

    /// <summary>
    /// Creates the target-settings interaction boundary for the shared persistence engine.
    /// </summary>
    internal TargetSettingsPersistence(
        LayeredSettingsPersistenceEngine engine,
        SettingsLayerDefinitionProvider layerDefinitions,
        TargetDiscoveryService targets,
        PersistedSettingsBatchSaver batchSaver)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _layerDefinitions = layerDefinitions ?? throw new ArgumentNullException(nameof(layerDefinitions));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _batchSaver = batchSaver ?? throw new ArgumentNullException(nameof(batchSaver));
    }

    /// <summary>
    /// Applies effective target-scoped persisted values to a live target settings owner.
    /// </summary>
    public void Apply(object settingsOwner, IOperationTarget? target)
    {
        if (settingsOwner == null || !TryCreateTargetLayers(target, out TargetTypeId targetTypeId, out IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> layers))
        {
            return;
        }

        IReadOnlyDictionary<string, JToken> resolvedValues = _engine.ResolveEffectiveValues(layers);
        IReadOnlyList<PersistedSettingDescriptor> descriptors = GetTargetSettingsDescriptors(settingsOwner.GetType(), targetTypeId);
        _engine.ApplyValues(settingsOwner, descriptors, resolvedValues);
    }

    /// <summary>
    /// Captures and saves a live target settings owner for the supplied runtime target.
    /// </summary>
    public void Save(object settingsOwner, IOperationTarget? target)
    {
        if (settingsOwner == null || !TryCreateTargetLayers(target, out TargetTypeId targetTypeId, out IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> layers))
        {
            return;
        }

        PersistedSettingsWriteBatch batch = new();
        IReadOnlyList<PersistedSettingDescriptor> descriptors = GetTargetSettingsDescriptors(settingsOwner.GetType(), targetTypeId);
        object defaultOwner = CreateDefaultOwner(settingsOwner.GetType());
        Dictionary<string, JToken> defaultValues = _engine.CaptureValueTokens(defaultOwner, descriptors);
        _engine.CaptureValues(batch, settingsOwner, descriptors, defaultValues, layers);
        _batchSaver.Save(batch);
    }

    /// <summary>
    /// Returns target descriptor sets for startup key validation without exposing target policy to the host.
    /// </summary>
    internal IEnumerable<(string OwnerLabel, IReadOnlyList<PersistedSettingDescriptor> Descriptors)> GetGeneratedKeyDescriptors()
    {
        Type settingsType = typeof(TargetSettings);
        TargetTypeId placeholderTargetTypeId = new("target");
        yield return (settingsType.FullName ?? settingsType.Name, GetTargetSettingsDescriptors(settingsType, placeholderTargetTypeId));
    }

    /// <summary>
    /// Returns descriptors for one target settings owner using the target system's generated owner prefix.
    /// </summary>
    private IReadOnlyList<PersistedSettingDescriptor> GetTargetSettingsDescriptors(Type settingsType, TargetTypeId targetTypeId)
    {
        if (_descriptorCache.TryGetValue((settingsType, targetTypeId), out IReadOnlyList<PersistedSettingDescriptor>? cachedDescriptors))
        {
            return cachedDescriptors;
        }

        string ownerPrefix = GetTargetSettingsOwnerPrefix(settingsType, targetTypeId);
        IReadOnlyList<PersistedSettingDescriptor> descriptors = PersistedSettingDescriptor.CreateForOwner(settingsType, ownerPrefix);
        _descriptorCache[(settingsType, targetTypeId)] = descriptors;
        return descriptors;
    }

    /// <summary>
    /// Creates a default target settings owner so sparse captures can remove values that match owner defaults.
    /// </summary>
    private static object CreateDefaultOwner(Type ownerType)
    {
        return Activator.CreateInstance(ownerType)
            ?? throw new InvalidOperationException($"Settings owner '{ownerType.FullName}' must have a public parameterless constructor.");
    }

    /// <summary>
    /// Produces the stable generated-key owner prefix for a target settings owner type.
    /// </summary>
    private string GetTargetSettingsOwnerPrefix(Type settingsType, TargetTypeId targetTypeId)
    {
        if (_targetOwnerPrefixCache.TryGetValue((settingsType, targetTypeId), out string? cachedPrefix))
        {
            return cachedPrefix;
        }

        string ownerSegment = PersistedSettingDescriptor.ResolveOwnerSegment(settingsType);
        string prefix = targetTypeId.Value + "." + ownerSegment;
        _targetOwnerPrefixCache[(settingsType, targetTypeId)] = prefix;
        return prefix;
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
