using System;
using System.IO;

namespace LocalAutomation.Application;

/// <summary>
/// Carries launcher-owned host configuration values into the application service composition path.
/// </summary>
public sealed class LocalAutomationHostOptions
{
    /// <summary>
    /// Creates host options while preserving the default paths used by the standard LocalAutomation host.
    /// </summary>
    public LocalAutomationHostOptions(
        string? appDataRootPath = null,
        string? targetSettingsFileName = null,
        string? shellDataFolderName = null,
        string? defaultOutputRootPath = null,
        string? defaultTempRootPath = null)
    {
        AppDataRootPath = string.IsNullOrWhiteSpace(appDataRootPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                LocalAutomationHostStorage.DefaultDataFolderName)
            : appDataRootPath;
        TargetSettingsFileName = string.IsNullOrWhiteSpace(targetSettingsFileName)
            ? LocalAutomationHostStorage.DefaultTargetSettingsFileName
            : targetSettingsFileName;
        ShellDataFolderName = string.IsNullOrWhiteSpace(shellDataFolderName)
            ? LocalAutomationHostStorage.DefaultDataFolderName
            : shellDataFolderName;
        DefaultOutputRootPath = defaultOutputRootPath;
        DefaultTempRootPath = defaultTempRootPath;
    }

    /// <summary>
    /// Gets the host-owned app-data root used for logs, global settings, and user target overrides.
    /// </summary>
    public string AppDataRootPath { get; }

    /// <summary>
    /// Gets the target-local settings file name used beside file-backed or directory-backed targets.
    /// </summary>
    public string TargetSettingsFileName { get; }

    /// <summary>
    /// Gets the launcher identity folder name associated with these host options.
    /// </summary>
    public string ShellDataFolderName { get; }

    /// <summary>
    /// Gets the launcher-provided default output root for newly created application settings.
    /// </summary>
    public string? DefaultOutputRootPath { get; }

    /// <summary>
    /// Gets the launcher-provided default temporary root for newly created application settings.
    /// </summary>
    public string? DefaultTempRootPath { get; }
}
