using System;
using System.IO;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LocalAutomation.Application.Tests;

public sealed class TargetSettingsPersistenceTests
{
    /// <summary>
    /// Confirms that directory-backed targets load target-local settings from the target directory itself.
    /// </summary>
    [Fact]
    public void TargetLocalSettingsAreLoadedFromTargetDirectory()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "LocalAutomation.Application.Tests", Guid.NewGuid().ToString("N"));
        string targetDirectory = Path.Combine(testRoot, "target");
        string appDataRoot = Path.Combine(testRoot, "app-data");
        string outputRoot = Path.Combine(testRoot, "output");
        string tempRoot = Path.Combine(testRoot, "temp");
        string targetSettingsFileName = "target-settings.json";
        string persistedValue = "from-target-local";

        try
        {
            // Write the target-local file inside the target directory because directory-backed targets use that path as identity.
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(
                Path.Combine(targetDirectory, targetSettingsFileName),
                CreatePersistedSettingsJson("test.value", persistedValue));

            // Register the fake target through the same catalog path used by application hosts.
            ExtensionCatalog catalog = new();
            catalog.RegisterTarget(new TargetDescriptor(new TargetTypeId("test-target"), "Test Target", typeof(DirectoryBackedTestTarget)));
            LocalAutomationApplicationHost host = new(
                catalog,
                appDataRootPath: appDataRoot,
                targetSettingsFileName: targetSettingsFileName,
                defaultOutputRootPath: outputRoot,
                defaultTempRootPath: tempRoot);

            // Apply target settings through the public target persistence flow rather than inspecting layer internals.
            DirectoryBackedTestTarget target = new(targetDirectory);
            TargetSettingsOwner settingsOwner = new();

            host.TargetSettingsService.Apply(settingsOwner, target);

            // The persisted value should replace the owner default when the target-local file is in the target directory.
            Assert.Equal(persistedValue, settingsOwner.Value);
        }
        finally
        {
            // Clean the isolated filesystem state even when the red assertion fails.
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Creates the minimal persisted settings file shape consumed by the application persistence engine.
    /// </summary>
    private static string CreatePersistedSettingsJson(string key, string value)
    {
        JObject values = new()
        {
            [key] = value
        };

        JObject state = new()
        {
            ["Version"] = 1,
            ["Values"] = values
        };

        return state.ToString();
    }

    /// <summary>
    /// Minimal directory-backed target whose stable target path is the target directory itself.
    /// </summary>
    private sealed class DirectoryBackedTestTarget : OperationTarget
    {
        /// <summary>
        /// Creates a fake target that follows the same directory-backed target-path contract as real target types.
        /// </summary>
        public DirectoryBackedTestTarget(string targetDirectory)
        {
            TargetPath = targetDirectory;
        }

        /// <summary>
        /// Gets a stable test display name for target-key construction and diagnostics.
        /// </summary>
        public override string Name => "Directory Backed Test Target";

        /// <summary>
        /// The fake target has no descriptor-backed state to reload.
        /// </summary>
        public override void LoadDescriptor()
        {
        }
    }

    /// <summary>
    /// Minimal target settings owner with one target-local value under an explicit stable key.
    /// </summary>
    private sealed class TargetSettingsOwner
    {
        /// <summary>
        /// Gets or sets the value loaded from the target-local settings layer.
        /// </summary>
        [PersistedValue(PersistenceScope.TargetLocal, "test.value")]
        public string Value { get; set; } = "default";
    }
}
