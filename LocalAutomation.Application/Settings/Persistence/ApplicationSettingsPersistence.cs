using System;
using System.Collections.Generic;
using LocalAutomation.Runtime;
using Newtonsoft.Json.Linq;

namespace LocalAutomation.Application;

/// <summary>
/// Applies and captures context-free application settings through the shared persistence primitives.
/// </summary>
internal sealed class ApplicationSettingsPersistence
{
    // Descriptor lists are cached by settings owner type because application settings metadata is stable at runtime.
    private readonly Dictionary<Type, IReadOnlyList<PersistedSettingDescriptor>> _descriptorCache = new();
    // The engine reads supplied layers and captures sparse writes for supplied descriptors.
    private readonly LayeredSettingsPersistenceEngine _engine;
    // Layer definitions are materialized by application policy before the generic engine reads or writes settings files.
    private readonly SettingsLayerDefinitionProvider _layerDefinitions;
    // Host-created defaults are captured before persisted values are applied so user-authored defaults remain meaningful.
    private readonly Dictionary<string, JToken> _ownerDefaultValues = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates the application-settings interaction boundary for the shared persistence engine.
    /// </summary>
    internal ApplicationSettingsPersistence(LayeredSettingsPersistenceEngine engine, SettingsLayerDefinitionProvider layerDefinitions)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _layerDefinitions = layerDefinitions ?? throw new ArgumentNullException(nameof(layerDefinitions));
    }

    /// <summary>
    /// Applies context-free persisted values to a live application settings owner.
    /// </summary>
    public void Apply(object settingsOwner)
    {
        if (settingsOwner == null)
        {
            throw new ArgumentNullException(nameof(settingsOwner));
        }

        IReadOnlyList<PersistedSettingDescriptor> descriptors = GetApplicationSettingsDescriptors(settingsOwner.GetType());
        _engine.AddMissingValueTokens(settingsOwner, descriptors, _ownerDefaultValues);
        IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> layers = _layerDefinitions.CreateApplicationLayers();
        IReadOnlyDictionary<string, JToken> resolvedValues = _engine.ResolveEffectiveValues(layers);
        _engine.ApplyValues(settingsOwner, descriptors, resolvedValues);
    }

    /// <summary>
    /// Captures application settings into a detached sparse write batch using the host-default baseline.
    /// </summary>
    public PersistedSettingsWriteBatch Capture(object settingsOwner)
    {
        if (settingsOwner == null)
        {
            throw new ArgumentNullException(nameof(settingsOwner));
        }

        PersistedSettingsWriteBatch batch = new();
        IReadOnlyList<PersistedSettingDescriptor> descriptors = GetApplicationSettingsDescriptors(settingsOwner.GetType());
        IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> layers = _layerDefinitions.CreateApplicationLayers();
        _engine.CaptureValues(batch, settingsOwner, descriptors, _ownerDefaultValues, layers);
        return batch;
    }

    /// <summary>
    /// Returns application descriptor sets for startup key validation without exposing application policy to the host.
    /// </summary>
    internal IEnumerable<(string OwnerLabel, IReadOnlyList<PersistedSettingDescriptor> Descriptors)> GetGeneratedKeyDescriptors()
    {
        Type settingsType = typeof(ApplicationSettings);
        yield return (settingsType.FullName ?? settingsType.Name, GetApplicationSettingsDescriptors(settingsType));
    }

    /// <summary>
    /// Returns descriptors for one application settings owner using its declared or implicit application owner segment.
    /// </summary>
    private IReadOnlyList<PersistedSettingDescriptor> GetApplicationSettingsDescriptors(Type settingsType)
    {
        if (_descriptorCache.TryGetValue(settingsType, out IReadOnlyList<PersistedSettingDescriptor>? cachedDescriptors))
        {
            return cachedDescriptors;
        }

        string ownerPrefix = PersistedSettingDescriptor.ResolveOwnerSegment(settingsType);
        IReadOnlyList<PersistedSettingDescriptor> descriptors = PersistedSettingDescriptor.CreateForOwner(settingsType, ownerPrefix);
        _descriptorCache[settingsType] = descriptors;
        return descriptors;
    }
}
