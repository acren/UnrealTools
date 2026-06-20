using System.Runtime.CompilerServices;
using TestUtilities;

namespace LocalAutomation.Avalonia.Tests;

/// <summary>
/// Initializes the application-facing test logging pipeline for the Avalonia test assembly.
/// </summary>
internal static class TestAssemblyLoggingBootstrap
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AppFacingTestLoggingBootstrap.Initialize(
            loggerCategoryName: "LocalAutomation.Avalonia.Tests",
            launchLogFilePrefix: "localautomation-avalonia-tests-launch");
    }
}
