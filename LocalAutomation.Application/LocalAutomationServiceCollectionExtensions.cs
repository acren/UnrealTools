using System;
using System.IO;
using System.Linq;
using LocalAutomation.Extensions.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LocalAutomation.Application;

/// <summary>
/// Registers the LocalAutomation application service graph with the Microsoft dependency injection container.
/// </summary>
public static class LocalAutomationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the process-wide LocalAutomation application services used by the desktop shell.
    /// </summary>
    public static IServiceCollection AddLocalAutomationApplication(
        this IServiceCollection services,
        ExtensionCatalog catalog,
        LocalAutomationHostOptions? options = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        // The populated catalog and resolved options are process-wide inputs to the desktop application graph.
        services.AddSingleton(catalog);
        services.AddSingleton(options ?? new LocalAutomationHostOptions());
        services.AddSingleton<ContextActionService>();
        services.AddSingleton<OptionEditorService>();
        services.AddSingleton<TargetDiscoveryService>();
        services.AddSingleton<OperationCatalogService>();
        services.AddSingleton(CreateExecutionSessionService);
        services.AddSingleton(CreateSettingsLayerDefinitionProvider);
        services.AddSingleton(CreateLayeredSettingsPersistenceEngine);
        services.AddSingleton(CreatePersistedSettingsBatchSaver);
        services.AddSingleton(CreateOperationSessionService);
        services.AddSingleton(CreateApplicationSettingsService);
        services.AddSingleton(CreateTargetSettingsService);
        return services;
    }

    /// <summary>
    /// Builds a service provider with validation enabled so composition failures surface during startup.
    /// </summary>
    public static ServiceProvider BuildLocalAutomationServiceProvider(this IServiceCollection services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    /// <summary>
    /// Validates generated persisted-setting keys after the application settings service applies persisted startup paths.
    /// </summary>
    public static void ValidateLocalAutomationPersistedSettingKeys(this ServiceProvider services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Resolving application settings first applies persisted path settings before any later startup action uses OutputPaths.
        ApplicationSettingsService applicationSettings = services.GetRequiredService<ApplicationSettingsService>();
        PersistedSettingKeyValidator.Validate(
            services.GetRequiredService<OperationSessionService>().GetGeneratedKeyDescriptors()
                .Concat(applicationSettings.GetGeneratedKeyDescriptors())
                .Concat(services.GetRequiredService<TargetSettingsService>().GetGeneratedKeyDescriptors()));
    }

    /// <summary>
    /// Creates the execution session service with the host-owned execution log directory.
    /// </summary>
    private static ExecutionSessionService CreateExecutionSessionService(IServiceProvider services)
    {
        LocalAutomationHostOptions options = services.GetRequiredService<LocalAutomationHostOptions>();
        string executionLogDirectory = Path.Combine(options.AppDataRootPath, "Logs", "Executions");
        return new ExecutionSessionService(executionLogDirectory);
    }

    /// <summary>
    /// Creates the settings layer provider from resolved host storage options.
    /// </summary>
    private static SettingsLayerDefinitionProvider CreateSettingsLayerDefinitionProvider(IServiceProvider services)
    {
        LocalAutomationHostOptions options = services.GetRequiredService<LocalAutomationHostOptions>();
        return new SettingsLayerDefinitionProvider(options.AppDataRootPath, options.TargetSettingsFileName);
    }

    /// <summary>
    /// Creates the generic layered settings engine from extension-provided setting value converters.
    /// </summary>
    private static LayeredSettingsPersistenceEngine CreateLayeredSettingsPersistenceEngine(IServiceProvider services)
    {
        ExtensionCatalog catalog = services.GetRequiredService<ExtensionCatalog>();
        return new LayeredSettingsPersistenceEngine(catalog.SettingValueConverters);
    }

    /// <summary>
    /// Creates the detached settings batch saver around the shared layered settings engine.
    /// </summary>
    private static PersistedSettingsBatchSaver CreatePersistedSettingsBatchSaver(IServiceProvider services)
    {
        return new PersistedSettingsBatchSaver(services.GetRequiredService<LayeredSettingsPersistenceEngine>());
    }

    /// <summary>
    /// Creates the operation session system and its internal option settings persistence workflow.
    /// </summary>
    private static OperationSessionService CreateOperationSessionService(IServiceProvider services)
    {
        return new OperationSessionService(
            services.GetRequiredService<OperationCatalogService>(),
            services.GetRequiredService<LayeredSettingsPersistenceEngine>(),
            services.GetRequiredService<SettingsLayerDefinitionProvider>(),
            services.GetRequiredService<ExtensionCatalog>(),
            services.GetRequiredService<TargetDiscoveryService>(),
            services.GetRequiredService<PersistedSettingsBatchSaver>());
    }

    /// <summary>
    /// Creates the application settings system from persistence plumbing and launcher defaults.
    /// </summary>
    private static ApplicationSettingsService CreateApplicationSettingsService(IServiceProvider services)
    {
        LocalAutomationHostOptions options = services.GetRequiredService<LocalAutomationHostOptions>();
        return new ApplicationSettingsService(
            services.GetRequiredService<LayeredSettingsPersistenceEngine>(),
            services.GetRequiredService<SettingsLayerDefinitionProvider>(),
            options.DefaultOutputRootPath,
            options.DefaultTempRootPath);
    }

    /// <summary>
    /// Creates the target settings system and its internal target settings persistence workflow.
    /// </summary>
    private static TargetSettingsService CreateTargetSettingsService(IServiceProvider services)
    {
        return new TargetSettingsService(
            services.GetRequiredService<LayeredSettingsPersistenceEngine>(),
            services.GetRequiredService<SettingsLayerDefinitionProvider>(),
            services.GetRequiredService<TargetDiscoveryService>(),
            services.GetRequiredService<PersistedSettingsBatchSaver>());
    }

}
