using System;
using System.IO;
using System.Linq;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;

namespace LocalAutomation.Application;

/// <summary>
/// Bundles the extension catalog and the application-facing discovery services so each UI host can bootstrap the
/// same LocalAutomation composition root with compile-time modules.
/// </summary>
public sealed class LocalAutomationApplicationHost
{
    /// <summary>
    /// Creates an application host around a populated extension catalog.
    /// </summary>
    public LocalAutomationApplicationHost(ExtensionCatalog catalog, string? appDataRootPath = null, string? targetSettingsFileName = null, string? shellDataFolderName = null, string? defaultOutputRootPath = null, string? defaultTempRootPath = null)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        string resolvedAppDataRootPath = appDataRootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LocalAutomationHostStorage.DefaultDataFolderName);
        string resolvedTargetSettingsFileName = string.IsNullOrWhiteSpace(targetSettingsFileName)
            ? LocalAutomationHostStorage.DefaultTargetSettingsFileName
            : targetSettingsFileName;
        // Store execution logs under the same host-owned app-data root as launch logs and persisted shell state.
        string executionLogDirectory = Path.Combine(resolvedAppDataRootPath, "Logs", "Executions");
        ContextActions = new ContextActionService(catalog);
        Execution = new ExecutionSessionService(executionLogDirectory);
        OptionEditors = new OptionEditorService(catalog);
        Targets = new TargetDiscoveryService(catalog);
        SettingsLayerDefinitionProvider layerDefinitionProvider = new(resolvedAppDataRootPath, resolvedTargetSettingsFileName);
        LayeredSettingsPersistenceEngine settingsEngine = new(catalog.SettingValueConverters);
        SettingsBatchSaver = new PersistedSettingsBatchSaver(settingsEngine);
        Operations = new OperationCatalogService(catalog);
        OperationSession = new OperationSessionService(Operations, settingsEngine, layerDefinitionProvider, Catalog, Targets, SettingsBatchSaver);
        ApplicationSettingsService = new ApplicationSettingsService(settingsEngine, layerDefinitionProvider, defaultOutputRootPath, defaultTempRootPath);
        TargetSettingsService = new TargetSettingsService(settingsEngine, layerDefinitionProvider, Targets, SettingsBatchSaver);
        PersistedSettingKeyValidator.Validate(
            OperationSession.GetGeneratedKeyDescriptors()
                .Concat(ApplicationSettingsService.GetGeneratedKeyDescriptors())
                .Concat(TargetSettingsService.GetGeneratedKeyDescriptors()));
        // Startup needs the persisted temp-root override in place before stale-session cleanup scans session folders.
        CleanupStaleSessionTempRoots();
    }

    /// <summary>
    /// Gets the catalog containing the registered extension modules and their descriptors.
    /// </summary>
    public ExtensionCatalog Catalog { get; }

    /// <summary>
    /// Gets the service used to query extension-provided target context actions.
    /// </summary>
    public ContextActionService ContextActions { get; }

    /// <summary>
    /// Gets the service used to query registered operations for a target.
    /// </summary>
    public OperationCatalogService Operations { get; }

    /// <summary>
    /// Gets the service that tracks the shared execution session for UI hosts.
    /// </summary>
    public ExecutionSessionService Execution { get; }

    /// <summary>
    /// Gets the service used to resolve property-grid editor targets for option sets.
    /// </summary>
    public OptionEditorService OptionEditors { get; }

    /// <summary>
    /// Deletes stale per-session temp roots left behind by prior crashes or abrupt exits so temp usage does not grow
    /// without bound across launches.
    /// </summary>
    private static void CleanupStaleSessionTempRoots()
    {
        string sessionsRootPath = OutputPaths.GetSessionTempRootParent();
        if (!Directory.Exists(sessionsRootPath))
        {
            return;
        }

        foreach (string sessionPath in Directory.GetDirectories(sessionsRootPath))
        {
            try
            {
                Directory.Delete(sessionPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only. Active or locked directories can remain and will be retried on the next launch.
            }
        }
    }

    /// <summary>
    /// Gets the shared batch saver used by debounced UI persistence requests.
    /// </summary>
    public PersistedSettingsBatchSaver SettingsBatchSaver { get; }

    /// <summary>
    /// Gets the service that owns host-wide application settings behavior.
    /// </summary>
    public ApplicationSettingsService ApplicationSettingsService { get; }

    /// <summary>
    /// Gets the service that owns target settings behavior.
    /// </summary>
    public TargetSettingsService TargetSettingsService { get; }

    /// <summary>
    /// Gets the service used to derive selected-operation UI state from the shared catalog and runtime adapters.
    /// </summary>
    public OperationSessionService OperationSession { get; }

    /// <summary>
    /// Gets the service used to create targets from host-provided paths or source values.
    /// </summary>
    public TargetDiscoveryService Targets { get; }

    /// <summary>
    /// Creates a host, registers the provided compile-time modules, and exposes the resulting services.
    /// </summary>
    public static LocalAutomationApplicationHost Create(params IExtensionModule[] modules)
    {
        ExtensionCatalog catalog = new();
        foreach (IExtensionModule module in modules)
        {
            catalog.RegisterModule(module);
        }

        return new LocalAutomationApplicationHost(catalog);
    }
}
