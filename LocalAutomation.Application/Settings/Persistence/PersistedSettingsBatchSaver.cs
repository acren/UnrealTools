using System;

namespace LocalAutomation.Application;

/// <summary>
/// Saves detached persisted-settings write batches through the generic persistence engine.
/// </summary>
public sealed class PersistedSettingsBatchSaver
{
    // The engine owns applying write/removal batches and deleting empty persisted files.
    private readonly LayeredSettingsPersistenceEngine _engine;

    /// <summary>
    /// Creates a batch saver for the shared application persistence engine.
    /// </summary>
    internal PersistedSettingsBatchSaver(LayeredSettingsPersistenceEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Saves a detached write batch by applying its writes and removals at the disk boundary.
    /// </summary>
    public void Save(PersistedSettingsWriteBatch batch)
    {
        _engine.SaveCapturedSettings(batch);
    }
}
