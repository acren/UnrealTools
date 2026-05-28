using System;
using System.Collections.Generic;
using LocalAutomation.Runtime;

namespace LocalAutomation.Application;

/// <summary>
/// Owns host-wide application settings state, runtime application, and application-setting persistence behavior.
/// </summary>
public sealed class ApplicationSettingsService
{
    // Application settings persistence stays inside the application-settings system boundary.
    private readonly ApplicationSettingsPersistence _persistence;

    /// <summary>
    /// Creates application settings, applies stored values, and updates runtime output paths for the host.
    /// </summary>
    internal ApplicationSettingsService(
        LayeredSettingsPersistenceEngine engine,
        SettingsLayerDefinitionProvider layerDefinitions,
        string? defaultOutputRootPath = null,
        string? defaultTempRootPath = null)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (layerDefinitions == null)
        {
            throw new ArgumentNullException(nameof(layerDefinitions));
        }

        Settings = new ApplicationSettings(defaultOutputRootPath, defaultTempRootPath);
        _persistence = new ApplicationSettingsPersistence(engine, layerDefinitions);
        _persistence.Apply(Settings);
        Apply();
    }

    /// <summary>
    /// Gets the shared host-global application settings instance edited by shell settings UI.
    /// </summary>
    public ApplicationSettings Settings { get; }

    /// <summary>
    /// Applies the current application settings to host-wide runtime services that need immediate updates.
    /// </summary>
    public void Apply()
    {
        OutputPaths.SetRoot(Settings.OutputRootPath);
        OutputPaths.SetTempRoot(Settings.TempRootPath);
    }

    /// <summary>
    /// Captures current application settings into a detached sparse write batch.
    /// </summary>
    public PersistedSettingsWriteBatch Capture()
    {
        return _persistence.Capture(Settings);
    }

    /// <summary>
    /// Returns application descriptor sets for startup key validation.
    /// </summary>
    internal IEnumerable<(string OwnerLabel, IReadOnlyList<PersistedSettingDescriptor> Descriptors)> GetGeneratedKeyDescriptors()
    {
        return _persistence.GetGeneratedKeyDescriptors();
    }
}
