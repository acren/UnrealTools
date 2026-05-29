using System;
using System.IO;
using Avalonia;
using LocalAutomation.Application;
using LocalAutomation.Avalonia.ViewModels;
using LocalAutomation.Core;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalAutomation.Avalonia.Bootstrap;

/// <summary>
/// Starts the shared shell around launcher-provided identity, extension discovery, and dependency injection composition.
/// </summary>
public static class ShellAppBootstrapper
{
    /// <summary>
    /// Starts the desktop lifetime using bundled extension discovery.
    /// </summary>
    public static void Run(string[] args)
    {
        Run(args, ShellIdentity.LocalAutomation);
    }

    /// <summary>
    /// Starts the desktop lifetime using the provided launcher identity and bundled extension discovery.
    /// </summary>
    public static void Run(string[] args, ShellIdentity shellIdentity)
    {
        App.ConfigureShellIdentity(shellIdentity);
        ApplicationLogService.Initialize();

        try
        {
            ExtensionLoadResult extensionLoadResult = BundledExtensionLoader.LoadBundledExtensions();
            ServiceProvider services = CreateServiceProvider(extensionLoadResult, shellIdentity);
            RunStartupActions(services);
            App.ConfigureServices(services);
            LogExtensionDiscovery(extensionLoadResult);
            BuildApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            ApplicationLogService.LogStartupException(ex);
            throw;
        }
    }

    /// <summary>
    /// Flushes the file-backed logging pipeline when the current process exits normally.
    /// </summary>
    static ShellAppBootstrapper()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ApplicationLogService.Shutdown();
    }

    /// <summary>
    /// Configures the shared desktop application builder used by launcher entry points.
    /// </summary>
    public static AppBuilder BuildApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    /// <summary>
    /// Creates the validated application service provider from all successfully discovered modules.
    /// </summary>
    private static ServiceProvider CreateServiceProvider(ExtensionLoadResult extensionLoadResult, ShellIdentity shellIdentity)
    {
        ExtensionCatalog catalog = new();
        foreach (IExtensionModule module in extensionLoadResult.Modules)
        {
            try
            {
                catalog.RegisterModule(module);
            }
            catch (Exception ex)
            {
                extensionLoadResult.Errors.Add($"Failed to register extension module '{module.Id}': {ex.Message}");
            }
        }

        // Build host storage options from the concrete launcher identity before any setting service is resolved.
        string appDataRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            shellIdentity.DataFolderName);
        LocalAutomationHostOptions hostOptions = new(
            appDataRootPath,
            shellIdentity.TargetSettingsFileName,
            shellIdentity.DataFolderName,
            shellIdentity.DefaultOutputRootPath,
            shellIdentity.DefaultTempRootPath);

        // Register Avalonia-owned windows and shell view models in the same root provider as the application services.
        return new ServiceCollection()
            .AddLocalAutomationApplication(catalog, hostOptions)
            .AddSingleton(shellIdentity)
            .AddSingleton<SessionPersistenceService>()
            .AddSingleton<MainWindowViewModel>()
            .AddTransient<MainWindow>()
            .AddTransient<SettingsWindowViewModel>()
            .AddTransient<SettingsWindow>()
            .BuildLocalAutomationServiceProvider();
    }

    /// <summary>
    /// Runs required startup actions after persisted application settings have been applied by their owning service.
    /// </summary>
    private static void RunStartupActions(ServiceProvider services)
    {
        services.ValidateLocalAutomationPersistedSettingKeys();

        // ApplicationSettingsService construction applies persisted output-path settings before this temp cleanup runs.
        CleanupStaleSessionTempRoots();
    }

    /// <summary>
    /// Deletes stale per-session temp roots left behind by prior crashes or abrupt exits.
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
                // Stale cleanup is best effort because active antivirus or file handles can temporarily lock a session directory.
            }
        }
    }

    /// <summary>
    /// Emits a concise discovery summary plus detailed warnings and errors into the startup log.
    /// </summary>
    private static void LogExtensionDiscovery(ExtensionLoadResult extensionLoadResult)
    {
        ApplicationLogService.LogInformation($"Discovered {extensionLoadResult.Modules.Count} extension module(s) during startup.");

        foreach (string warning in extensionLoadResult.Warnings)
        {
            ApplicationLogger.Logger.LogWarning("Extension discovery warning: {Warning}", warning);
        }

        foreach (string error in extensionLoadResult.Errors)
        {
            ApplicationLogger.Logger.LogError("Extension discovery error: {Error}", error);
        }
    }
}
