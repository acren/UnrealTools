using System.Runtime.CompilerServices;
using TestUtilities;

namespace LocalAutomation.Application.Tests;

/// <summary>
/// Initializes the application-facing test logging pipeline for the application test assembly.
/// </summary>
internal static class TestAssemblyLoggingBootstrap
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AppFacingTestLoggingBootstrap.Initialize(
            loggerCategoryName: "LocalAutomation.Application.Tests",
            launchLogFilePrefix: "localautomation-application-tests-launch");
    }
}
