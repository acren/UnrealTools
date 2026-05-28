using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LocalAutomation.Application;

/// <summary>
/// Collects detached per-file persisted setting writes so UI callers can capture reflected values once and let a
/// background saver merge and write them later.
/// </summary>
public sealed class PersistedSettingsWriteBatch
{
    /// <summary>
    /// Stores per-file setting keys that should be removed when the batch is applied.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _fileRemovals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores per-file setting values that should be written when the batch is applied.
    /// </summary>
    private readonly Dictionary<string, PersistedSettingValueCollection> _fileWrites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether the batch currently contains any file updates.
    /// </summary>
    public bool IsEmpty => _fileWrites.Count == 0 && _fileRemovals.Count == 0;

    /// <summary>
    /// Gets the per-file setting keys that should be removed during persistence.
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> FileRemovals => _fileRemovals;

    /// <summary>
    /// Gets the per-file key/value patches that should be applied during persistence.
    /// </summary>
    public IReadOnlyDictionary<string, PersistedSettingValueCollection> FileWrites => _fileWrites;

    /// <summary>
    /// Records one persisted key removal for the provided file path.
    /// </summary>
    public void RemoveValue(string filePath, string key)
    {
        ValidateFilePathAndKey(filePath, key);

        if (_fileWrites.TryGetValue(filePath, out PersistedSettingValueCollection? fileValues))
        {
            fileValues.Values.Remove(key);
            if (fileValues.Values.Count == 0)
            {
                _fileWrites.Remove(filePath);
            }
        }

        if (!_fileRemovals.TryGetValue(filePath, out HashSet<string>? fileKeys))
        {
            fileKeys = new HashSet<string>(StringComparer.Ordinal);
            _fileRemovals[filePath] = fileKeys;
        }

        fileKeys.Add(key);
    }

    /// <summary>
    /// Adds or replaces one persisted key write for the provided file path.
    /// </summary>
    public void WriteValue(string filePath, string key, JToken value)
    {
        ValidateFilePathAndKey(filePath, key);
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (_fileRemovals.TryGetValue(filePath, out HashSet<string>? fileKeys))
        {
            fileKeys.Remove(key);
            if (fileKeys.Count == 0)
            {
                _fileRemovals.Remove(filePath);
            }
        }

        if (!_fileWrites.TryGetValue(filePath, out PersistedSettingValueCollection? fileValues))
        {
            fileValues = new PersistedSettingValueCollection();
            _fileWrites[filePath] = fileValues;
        }

        fileValues.Values[key] = value.DeepClone();
    }

    /// <summary>
    /// Returns one merged batch where later operations override earlier operations for the same file path and setting key.
    /// </summary>
    public PersistedSettingsWriteBatch Merge(PersistedSettingsWriteBatch laterWrites)
    {
        if (laterWrites == null)
        {
            throw new ArgumentNullException(nameof(laterWrites));
        }

        PersistedSettingsWriteBatch merged = new();
        merged.Apply(this);
        merged.Apply(laterWrites);
        return merged;
    }

    /// <summary>
    /// Applies another batch into the current batch so later file operations replace earlier operations for matching keys.
    /// </summary>
    private void Apply(PersistedSettingsWriteBatch laterWrites)
    {
        foreach ((string filePath, HashSet<string> fileKeys) in laterWrites._fileRemovals)
        {
            foreach (string key in fileKeys)
            {
                RemoveValue(filePath, key);
            }
        }

        foreach ((string filePath, PersistedSettingValueCollection fileValues) in laterWrites._fileWrites)
        {
            foreach ((string key, JToken value) in fileValues.Values)
            {
                WriteValue(filePath, key, value);
            }
        }
    }

    /// <summary>
    /// Validates the path and key shared by write and removal operations.
    /// </summary>
    private static void ValidateFilePathAndKey(string filePath, string key)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must be provided.", nameof(filePath));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Setting key must be provided.", nameof(key));
        }
    }
}
