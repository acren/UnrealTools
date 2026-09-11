using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using SystemUtilities.IO;

namespace UnrealUtilities;

/// <summary>Prepares isolated package outputs and package-only requests for projects whose targets are already built.</summary>
public static class ProjectPackaging
{
    /// <summary>Returns the staging root beneath the caller-owned package output directory.</summary>
    public static string GetStagingRootPath(string outputPath)
    {
        return Path.Combine(outputPath, "StagedBuilds");
    }

    /// <summary>Returns the final platform-specific cook directory expected by UAT's CookOutputDir switch.</summary>
    public static string GetCookOutputPath(string outputPath, Engine engine)
    {
        return Path.Combine(outputPath, "Cooked", engine.GetWindowsPlatformName());
    }

    /// <summary>Requires a packaged executable tree under the supplied output root before launch or archive work.</summary>
    public static string GetRequiredPackagePath(string outputPath, Engine engine, string failureMessage)
    {
        // UAT adds the platform beneath the staging root; the persistent project's Saved directory is not this output.
        string packagePath = Path.Combine(GetStagingRootPath(outputPath), engine.GetWindowsPlatformName());
        if (!PackagePaths.Instance.IsTargetDirectory(packagePath))
        {
            throw new InvalidOperationException($"{failureMessage}: {packagePath}");
        }

        return packagePath;
    }

    /// <summary>Clears current-run outputs and persistent staging/cook roots before a fresh package pass.</summary>
    public static void PreparePackageOutputs(Project project, string outputPath, ILogger logger, CancellationToken cancellationToken)
    {
        // Clear the complete caller-owned output first so later package discovery cannot accept stale run artifacts.
        cancellationToken.ThrowIfCancellationRequested();
        FileUtils.DeleteDirectoryIfExists(outputPath);
        DeletePersistentProjectPackageOutputs(project, logger, cancellationToken);
    }

    /// <summary>Creates cook, stage, pak and package phases for targets compiled by an earlier build step.</summary>
    public static BuildCookRunProjectRequest CreatePackageOnlyRequest(BuildConfiguration configuration, string stagingDirectory,
        string cookOutputDirectory, bool noDebugInfo = false, string? additionalCookerOptions = null)
    {
        // Packaging consumes receipts from completed target builds, so this request must not compile targets again.
        return new BuildCookRunProjectRequest(
            BuildCookRunProjectPhases.Cook | BuildCookRunProjectPhases.Stage | BuildCookRunProjectPhases.Pak | BuildCookRunProjectPhases.Package,
            configuration: configuration,
            noDebugInfo: noDebugInfo,
            additionalCookerOptions: additionalCookerOptions,
            stagingDirectory: stagingDirectory,
            cookOutputDirectory: cookOutputDirectory);
    }

    /// <summary>Removes staged payloads and cooked content while leaving reusable project build outputs intact.</summary>
    private static void DeletePersistentProjectPackageOutputs(Project project, ILogger logger, CancellationToken cancellationToken)
    {
        string savedPath = Path.Combine(project.ProjectPath, "Saved");
        // Deletion is synchronous; check cancellation between roots before beginning another destructive step.
        cancellationToken.ThrowIfCancellationRequested();
        FileUtils.DeleteDirectoryIfExists(Path.Combine(savedPath, "StagedBuilds"), logger);
        cancellationToken.ThrowIfCancellationRequested();
        FileUtils.DeleteDirectoryIfExists(Path.Combine(savedPath, "Cooked"), logger);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
