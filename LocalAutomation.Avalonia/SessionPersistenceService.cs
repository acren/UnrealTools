using System;
using System.IO;
using System.Linq;
using LocalAutomation.Application;
using LocalAutomation.Avalonia.Bootstrap;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Persistence;
using LocalAutomation.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LocalAutomation.Avalonia;

/// <summary>
/// Loads and saves the Avalonia shell's stable session snapshot so layout or editor refactors do not wipe
/// target-scoped working state.
/// </summary>
public sealed class SessionPersistenceService
{
    private readonly TargetDiscoveryService _targets;
    private readonly string _dataFilePath;

    /// <summary>
    /// Creates a session persistence service around the shell identity and exact application services it needs.
    /// </summary>
    public SessionPersistenceService(
        ShellIdentity shellIdentity,
        TargetDiscoveryService targets)
    {
        shellIdentity = shellIdentity ?? throw new ArgumentNullException(nameof(shellIdentity));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));

        // Keep each launcher's persisted shell state inside its own LocalAppData root so host-specific shells do not overwrite
        // one another's target lists, selected operations, or option values.
        string dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            shellIdentity.DataFolderName);
        _dataFilePath = Path.Combine(dataFolder, shellIdentity.SessionFileName);
    }

    /// <summary>
    /// Loads an explicitly versioned snapshot, returning an empty session only when no file exists.
    /// Malformed or unsupported snapshots throw rather than discarding persisted working state.
    /// </summary>
    public SessionSnapshot Load()
    {
        if (!File.Exists(_dataFilePath))
        {
            return new SessionSnapshot();
        }

        string jsonText = File.ReadAllText(_dataFilePath);
        JObject token = JObject.Parse(jsonText);

        /* Validate the serialized declaration before deserialization: the model initializer would otherwise
           supply a version for missing input. A single case-insensitive declaration keeps validation unambiguous. */
        JProperty[] versionProperties = token.Properties()
            .Where(property => string.Equals(property.Name, nameof(SessionSnapshot.Version), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (versionProperties.Length != 1 ||
            versionProperties[0].Value.Type != JTokenType.Integer ||
            !JToken.DeepEquals(versionProperties[0].Value, new JValue(2)))
        {
            throw new JsonSerializationException("Session snapshot must explicitly declare one integer Version of 2.");
        }

        SessionSnapshot snapshot = token.ToObject<SessionSnapshot>(CreateSnapshotSerializer())
            ?? throw new JsonSerializationException("Session snapshot must contain an object.");

        // Reject null collections and incomplete target identities before callers enumerate or restore them.
        if (snapshot.Targets == null || snapshot.SelectedOperationIdsByTargetType == null ||
            snapshot.PendingTargetPath == null || snapshot.Targets.Any(target => target == null ||
                string.IsNullOrWhiteSpace(target.Key) || string.IsNullOrWhiteSpace(target.TargetTypeId) ||
                string.IsNullOrWhiteSpace(target.Path)))
        {
            throw new JsonSerializationException("Session snapshot contains null state or an incomplete target identity or path.");
        }

        return snapshot;
    }

    /// <summary>
    /// Saves the provided snapshot using the stable, versioned session format.
    /// </summary>
    public void Save(SessionSnapshot snapshot)
    {
        JsonFileStateStore<SessionSnapshot> store = new(
            filePath: _dataFilePath,
            createDefaultState: static () => new SessionSnapshot(),
            createSerializer: CreateSnapshotSerializer);

        store.Save(snapshot);
    }

    /// <summary>
    /// Creates a detached copy of the provided snapshot so background persistence can save it without reading mutable
    /// UI-owned collections after the request is queued.
    /// </summary>
    public SessionSnapshot CloneSnapshot(SessionSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return new SessionSnapshot
        {
            Version = snapshot.Version,
            SelectedTargetKey = snapshot.SelectedTargetKey,
            PendingTargetPath = snapshot.PendingTargetPath,
            SelectedOperationIdsByTargetType = new System.Collections.Generic.Dictionary<string, string?>(snapshot.SelectedOperationIdsByTargetType, StringComparer.Ordinal),
            Targets = snapshot.Targets.Select(target => new TargetSessionSnapshot
            {
                Key = target.Key,
                TargetTypeId = target.TargetTypeId,
                Path = target.Path
            }).ToList()
        };
    }

    /// <summary>
    /// Creates or updates a persisted target snapshot from the provided runtime target.
    /// </summary>
    public TargetSessionSnapshot CreateTargetSnapshot(IOperationTarget target)
    {
        TargetTypeId targetTypeId = _targets.GetTargetTypeId(target) ?? throw new InvalidOperationException($"No target descriptor matched '{target.GetType().Name}'.");
        string targetPath = _targets.GetTargetPath(target);
        return new TargetSessionSnapshot
        {
            Key = TargetKeyUtility.BuildTargetKey(targetTypeId, targetPath).Value,
            TargetTypeId = targetTypeId.Value,
            Path = targetPath
        };
    }

    /// <summary>
    /// Attempts to recreate a runtime target from its persisted snapshot.
    /// </summary>
    public bool TryRestoreTarget(TargetSessionSnapshot snapshot, out IOperationTarget? target)
    {
        target = null;
        if (!_targets.TryCreateTarget(snapshot.Path, out IOperationTarget? createdTarget) || createdTarget == null)
        {
            return false;
        }

        if (!_targets.IsTarget(createdTarget) || !_targets.IsValidTarget(createdTarget))
        {
            return false;
        }

        TargetTypeId? restoredTypeId = _targets.GetTargetTypeId(createdTarget);
        if (restoredTypeId == null || restoredTypeId.Value != snapshot.TypedTargetTypeId)
        {
            return false;
        }

        target = createdTarget;
        return true;
    }

    /// <summary>
    /// Creates the Json.NET serializer used for the stable session snapshot.
    /// </summary>
    private static JsonSerializer CreateSnapshotSerializer()
    {
        return new JsonSerializer();
    }

}
