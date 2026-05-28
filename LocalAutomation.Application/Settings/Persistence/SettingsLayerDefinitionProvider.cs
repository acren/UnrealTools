using System;
using System.Collections.Generic;
using System.IO;
using LocalAutomation.Runtime;

namespace LocalAutomation.Application;

/// <summary>
/// Builds the concrete persisted settings layer definitions owned by the application host.
/// </summary>
internal sealed class SettingsLayerDefinitionProvider
{
    // Target-local settings live beside each target artifact using the host-configured file name.
    private readonly string _targetSettingsFileName;
    // User override settings live under app data and are keyed by a sanitized target identity.
    private readonly string _userTargetSettingsDirectoryPath;
    // Context-free application settings live in the host app-data root.
    private readonly string _globalSettingsFilePath;

    /// <summary>
    /// Creates a provider for one application host's persisted settings file layout.
    /// </summary>
    public SettingsLayerDefinitionProvider(string appDataRootPath, string targetSettingsFileName)
    {
        if (string.IsNullOrWhiteSpace(appDataRootPath))
        {
            throw new ArgumentException("App data root path must be provided.", nameof(appDataRootPath));
        }

        _targetSettingsFileName = string.IsNullOrWhiteSpace(targetSettingsFileName)
            ? throw new ArgumentException("Target settings file name must be provided.", nameof(targetSettingsFileName))
            : targetSettingsFileName;
        _userTargetSettingsDirectoryPath = Path.Combine(appDataRootPath, "target-overrides");
        _globalSettingsFilePath = Path.Combine(appDataRootPath, "global-settings.json");
    }

    /// <summary>
    /// Creates the context-free ordered layer stack for host-wide application settings.
    /// </summary>
    public IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> CreateApplicationLayers()
    {
        return new LayeredSettingsPersistenceEngine.LayerDefinition[]
        {
            new(PersistenceScope.Global, "global", _globalSettingsFilePath)
        };
    }

    /// <summary>
    /// Creates the target-scoped ordered layer stack from lowest to highest precedence.
    /// </summary>
    public IReadOnlyList<LayeredSettingsPersistenceEngine.LayerDefinition> CreateTargetLayers(string targetPath, TargetKey targetKey)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Target path must be provided.", nameof(targetPath));
        }

        return new LayeredSettingsPersistenceEngine.LayerDefinition[]
        {
            new(PersistenceScope.Global, "global", _globalSettingsFilePath),
            new(PersistenceScope.TargetLocal, "target_local", GetTargetLocalSettingsFilePath(targetPath)),
            new(PersistenceScope.UserTargetOverride, "user_override", GetUserTargetOverrideFilePath(targetKey))
        };
    }

    /// <summary>
    /// Resolves the target-local settings file path inside directory-backed targets or beside file-backed targets.
    /// </summary>
    private string GetTargetLocalSettingsFilePath(string targetPath)
    {
        string? targetDirectory = Directory.Exists(targetPath)
            ? targetPath
            : Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            targetDirectory = Directory.GetCurrentDirectory();
        }

        return Path.Combine(targetDirectory, _targetSettingsFileName);
    }

    /// <summary>
    /// Resolves the per-target user override file path under the application's data directory.
    /// </summary>
    private string GetUserTargetOverrideFilePath(TargetKey targetKey)
    {
        string fileName = SanitizeFileName(targetKey.Value) + ".json";
        return Path.Combine(_userTargetSettingsDirectoryPath, fileName);
    }

    /// <summary>
    /// Replaces invalid file-name characters in target keys so appdata override files are always valid paths.
    /// </summary>
    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = value;
        foreach (char invalidChar in invalidChars)
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        return sanitized;
    }
}
