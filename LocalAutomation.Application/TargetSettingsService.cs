using System;
using System.Collections.Generic;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;

namespace LocalAutomation.Application;

/// <summary>
/// Owns public target-settings apply/save behavior while keeping target persistence details internal.
/// </summary>
public sealed class TargetSettingsService
{
    // Target settings persistence stays inside the target-settings system boundary.
    private readonly TargetSettingsPersistence _persistence;

    /// <summary>
    /// Creates the target settings service around shared persistence plumbing and target discovery.
    /// </summary>
    internal TargetSettingsService(
        LayeredSettingsPersistenceEngine engine,
        SettingsLayerDefinitionProvider layerDefinitions,
        TargetDiscoveryService targets,
        PersistedSettingsBatchSaver batchSaver)
    {
        _persistence = new TargetSettingsPersistence(
            engine ?? throw new ArgumentNullException(nameof(engine)),
            layerDefinitions ?? throw new ArgumentNullException(nameof(layerDefinitions)),
            targets ?? throw new ArgumentNullException(nameof(targets)),
            batchSaver ?? throw new ArgumentNullException(nameof(batchSaver)));
    }

    /// <summary>
    /// Applies effective target-scoped persisted values to a live target settings owner.
    /// </summary>
    public void Apply(object settingsOwner, IOperationTarget? target)
    {
        _persistence.Apply(settingsOwner, target);
    }

    /// <summary>
    /// Captures and saves a live target settings owner for the supplied runtime target.
    /// </summary>
    public void Save(object settingsOwner, IOperationTarget? target)
    {
        _persistence.Save(settingsOwner, target);
    }

    /// <summary>
    /// Returns target descriptor sets for startup key validation.
    /// </summary>
    internal IEnumerable<(string OwnerLabel, IReadOnlyList<PersistedSettingDescriptor> Descriptors)> GetGeneratedKeyDescriptors()
    {
        return _persistence.GetGeneratedKeyDescriptors();
    }
}
