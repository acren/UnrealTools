using System;
using System.IO;
using LocalAutomation.Core;
using LocalAutomation.Core.IO;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnrealAutomationCommon.Unreal;
using EngineModel = UnrealAutomationCommon.Unreal.Engine;
using PluginModel = UnrealAutomationCommon.Unreal.Plugin;
using ProjectModel = UnrealAutomationCommon.Unreal.Project;

namespace LocalAutomation.Extensions.Unreal.Targets;

/// <summary>Owns project runtime notifications, descriptor watching, and filesystem-management operations.</summary>
[Target]
public class Project : OperationTarget, IPackageProvider, IEngineInstanceProvider, IDisposable
{
    private readonly ProjectModel _model;
    // Serializing reload and disposal prevents queued callbacks from accessing a completed target's files.
    private readonly object _descriptorLock = new();
    private FileSystemWatcher? _watcher;
    private volatile Exception? _backgroundException;
    private bool _disposed;

    /// <summary>Gets model state, surfacing any pending watcher failure on the caller's thread.</summary>
    [JsonIgnore]
    public ProjectModel Model
    {
        get
        {
            ThrowIfBackgroundException();
            return _model;
        }
    }

    /// <summary>Waits for readable descriptor input before constructing the model and starting its watcher.</summary>
    [JsonConstructor]
    public Project(string targetPath) : this(LoadModel(targetPath))
    {
    }

    /// <summary>Wraps a model and owns descriptor watching for its runtime lifetime.</summary>
    public Project(ProjectModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        TargetPath = model.TargetPath;
        InitializeWatcher();
    }

    public override string Name => Model.Name;
    public override string DisplayName => Model.DisplayName;
    public override bool IsValid => Model.IsValid;
    public EngineModel EngineInstance => Model.EngineInstance;
    public string EngineInstanceName => Model.EngineInstanceName;
    public ProjectDescriptor ProjectDescriptor => Model.ProjectDescriptor;

    /// <summary>Loads initial model state within runtime telemetry and readable-file waiting.</summary>
    private static ProjectModel LoadModel(string targetPath)
    {
        string descriptorPath = ProjectPaths.Instance.FindRequiredTargetFile(targetPath);
        using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("ProjectDescriptor.Load")
            .SetTag("descriptor.path", descriptorPath);
        FileUtils.WaitForFileReadable(descriptorPath);
        return new ProjectModel(targetPath);
    }

    /// <summary>Reloads the descriptor and publishes the properties that depend on its engine association.</summary>
    public override void LoadDescriptor()
    {
        lock (_descriptorLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("ProjectDescriptor.Load")
                .SetTag("descriptor.path", _model.UProjectPath);
            FileUtils.WaitForFileReadable(_model.UProjectPath);
            _model.LoadDescriptor();
            _backgroundException = null;
            OnPropertyChanged(nameof(Model));
            OnPropertyChanged(nameof(ProjectDescriptor));
            OnPropertyChanged(nameof(EngineInstance));
            OnPropertyChanged(nameof(EngineInstanceName));
        }
    }

    /// <summary>Resolves the engine while recording descriptor-driven lookup cost.</summary>
    public EngineModel GetEngineInstanceForDiagnostics()
    {
        using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("Project.GetEngineInstance")
            .SetTag("descriptor.path", Model.UProjectPath);
        EngineModel engine = EngineInstance;
        activity.SetTag("engine.name", engine.DisplayName);
        return engine;
    }

    /// <summary>Wraps the staged model only when a runtime operation requests package provision.</summary>
    public Package? GetProvidedPackage(EngineModel engineContext)
    {
        UnrealAutomationCommon.Unreal.Package? package = Model.GetStagedPackage(engineContext);
        return package == null ? null : new Package(package);
    }

    /// <summary>Copies plugin files into this project's plugin directory with Core overwrite semantics.</summary>
    public void AddPlugin(string pluginPath)
    {
        FileUtils.CopyDirectory(pluginPath, Model.PluginsPath, true);
    }

    /// <summary>Copies the supplied runtime plugin into this project.</summary>
    public void AddPlugin(Plugin plugin)
    {
        AddPlugin(plugin.Model.PluginPath);
    }

    /// <summary>Removes matching project-contained plugins using Core deletion semantics.</summary>
    public void RemovePlugin(string pluginName)
    {
        foreach (PluginModel plugin in Model.Plugins)
        {
            if (plugin.Name == pluginName)
            {
                FileUtils.DeleteDirectory(plugin.PluginPath);
            }
        }
    }

    /// <summary>Removes source files and the descriptor's Unreal module declarations.</summary>
    public void ConvertToBlueprintOnly()
    {
        ProjectModel model = Model;
        FileUtils.DeleteDirectoryIfExists(model.SourcePath);
        // Edit the persisted field directly so unmodeled descriptor properties survive the conversion.
        JObject descriptor = JObject.Parse(File.ReadAllText(model.UProjectPath));
        descriptor.Remove("Modules");
        File.WriteAllText(model.UProjectPath, descriptor.ToString());
    }

    /// <summary>Starts background descriptor refresh for editor-visible project state.</summary>
    private void InitializeWatcher()
    {
        _watcher = new FileSystemWatcher(TargetPath);
        _watcher.Changed += HandleWatcherChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Reloads descriptor edits and captures failures so a watcher thread cannot terminate the process.</summary>
    private void HandleWatcherChanged(object sender, FileSystemEventArgs args)
    {
        lock (_descriptorLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                string? projectPath = ProjectPaths.Instance.FindTargetFile(TargetPath);
                if (projectPath == null || !string.Equals(args.FullPath, projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                LoadDescriptor();
            }
            catch (Exception ex)
            {
                RecordBackgroundException(ex);
            }
        }
    }

    /// <summary>Records reload and logging failures for foreground access without throwing from the callback.</summary>
    private void RecordBackgroundException(Exception exception)
    {
        _backgroundException = exception;
        try
        {
            ApplicationLogger.Logger.LogError(exception, "Project background watcher failed for '{ProjectPath}'.", TargetPath);
        }
        catch (Exception loggingException)
        {
            // Logging can fail during application teardown; both failures must remain observable to the owner.
            _backgroundException = new AggregateException(exception, loggingException);
        }
    }

    /// <summary>Keeps failed descriptor state unavailable until an explicit or watched reload succeeds.</summary>
    private void ThrowIfBackgroundException()
    {
        if (_backgroundException is Exception exception)
        {
            throw new InvalidOperationException($"Project target '{TargetPath}' encountered a background reload failure.", exception);
        }
    }

    /// <summary>Stops descriptor watching so later temporary-directory churn cannot reload a completed target.</summary>
    public void Dispose()
    {
        lock (_descriptorLock)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
        }
    }
}
