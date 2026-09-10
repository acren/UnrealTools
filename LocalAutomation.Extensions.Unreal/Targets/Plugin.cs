using System;
using System.IO;
using LocalAutomation.Core;
using LocalAutomation.Core.IO;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using UnrealAutomationCommon.Unreal;
using EngineModel = UnrealAutomationCommon.Unreal.Engine;
using PluginModel = UnrealAutomationCommon.Unreal.Plugin;

namespace LocalAutomation.Extensions.Unreal.Targets;

/// <summary>Owns plugin descriptor watching, runtime parent lifetime, and plugin deletion.</summary>
[Target]
public class Plugin : OperationTarget, IEngineInstanceProvider, IDisposable
{
    private readonly PluginModel _model;
    // Reload, parent creation, and disposal share a lock so teardown cannot leave a newly created watcher alive.
    private readonly object _descriptorLock = new();
    private FileSystemWatcher? _watcher;
    private Project? _hostProject;
    private volatile Exception? _backgroundException;
    private bool _disposed;

    /// <summary>Gets model state, surfacing pending plugin or owned-project watcher failures.</summary>
    [JsonIgnore]
    public PluginModel Model
    {
        get
        {
            ThrowIfBackgroundException();
            Project? hostProject = _hostProject;
            if (hostProject != null)
            {
                // The model and runtime hierarchy share this descriptor, so its reload failures affect both.
                _ = hostProject.Model;
            }

            return _model;
        }
    }

    /// <summary>Waits for readable descriptor input before constructing the model and starting its watcher.</summary>
    [JsonConstructor]
    public Plugin(string targetPath) : this(LoadModel(targetPath))
    {
    }

    /// <summary>Wraps a plugin model and owns descriptor watching for its runtime lifetime.</summary>
    public Plugin(PluginModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        TargetPath = model.TargetPath;
        InitializeWatcher();
    }

    public override string Name => Model.Name;
    public override bool IsValid => Model.IsValid;
    public PluginDescriptor PluginDescriptor => Model.PluginDescriptor;
    public string EngineInstanceName => EngineInstance?.DisplayName ?? "None";
    public override IOperationTarget ParentTarget => HostProject;

    /// <summary>Resolves the model's engine after establishing runtime ownership of any required host descriptor.</summary>
    public EngineModel EngineInstance
    {
        get
        {
            if (Model.PluginDescriptor.EngineVersion == null)
            {
                /* Host-based engine resolution needs a watched project model. Creating the runtime parent first
                   ensures its initial descriptor read is also covered by waiting and telemetry. */
                _ = HostProject.Model;
            }

            return Model.EngineInstance;
        }
    }

    /// <summary>Reuses one owned runtime project backed by the plugin model's cached project descriptor.</summary>
    public Project HostProject => ResolveHostProject();

    /// <summary>Loads initial model state within runtime telemetry, waiting, and diagnostic reporting.</summary>
    private static PluginModel LoadModel(string targetPath)
    {
        string descriptorPath = PluginPaths.Instance.FindRequiredTargetFile(targetPath);
        using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("PluginDescriptor.Load")
            .SetTag("descriptor.path", descriptorPath);
        FileUtils.WaitForFileReadable(descriptorPath);
        try
        {
            return new PluginModel(targetPath);
        }
        catch (Exception ex)
        {
            throw ReportDescriptorLoadFailure(descriptorPath, ex);
        }
    }

    /// <summary>Reloads the descriptor and publishes the properties that depend on its engine version.</summary>
    public override void LoadDescriptor()
    {
        lock (_descriptorLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string descriptorPath = _model.UPluginPath;
            using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("PluginDescriptor.Load")
                .SetTag("descriptor.path", descriptorPath);
            FileUtils.WaitForFileReadable(descriptorPath);
            try
            {
                _model.LoadDescriptor();
            }
            catch (Exception ex)
            {
                throw ReportDescriptorLoadFailure(descriptorPath, ex);
            }

            _backgroundException = null;
            OnPropertyChanged(nameof(Model));
            OnPropertyChanged(nameof(PluginDescriptor));
            OnPropertyChanged(nameof(EngineInstance));
            OnPropertyChanged(nameof(EngineInstanceName));
        }
    }

    /// <summary>Reports descriptor failures while retaining the read error even if application logging is unavailable.</summary>
    private static InvalidOperationException ReportDescriptorLoadFailure(string descriptorPath, Exception exception)
    {
        try
        {
            ApplicationLogger.Logger.LogError(exception, "Failed to deserialize plugin descriptor '{PluginDescriptorPath}'.", descriptorPath);
        }
        catch (Exception loggingException)
        {
            // A logging failure must not replace the descriptor failure the foreground caller needs to diagnose.
            exception = new AggregateException(exception, loggingException);
        }

        return new InvalidOperationException($"Failed to load plugin descriptor '{descriptorPath}'.", exception);
    }

    /// <summary>Resolves the host project while recording lookup cost and cache reuse.</summary>
    public Project GetHostProjectForDiagnostics()
    {
        using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("Plugin.GetHostProject")
            .SetTag("host_project.path", Model.HostProjectPath);
        bool cacheHit = _hostProject != null;
        Project hostProject = ResolveHostProject();
        activity.SetTag("cache.hit", cacheHit)
            .SetTag("target.type", hostProject.GetType().Name)
            .SetTag("is_valid", hostProject.IsValid);
        return hostProject;
    }

    /// <summary>Creates the model's project parent under initial-load policy and owns its runtime watcher.</summary>
    private Project ResolveHostProject()
    {
        lock (_descriptorLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PluginModel model = Model;
            if (_hostProject == null)
            {
                string descriptorPath = ProjectPaths.Instance.FindRequiredTargetFile(model.HostProjectPath);
                using PerformanceActivityScope activity = PerformanceTelemetry.StartActivity("ProjectDescriptor.Load")
                    .SetTag("descriptor.path", descriptorPath);
                FileUtils.WaitForFileReadable(descriptorPath);
                _hostProject = new Project(model.HostProject);
            }

            return _hostProject;
        }
    }

    /// <summary>Deletes the plugin directory using Core filesystem semantics.</summary>
    public void DeletePlugin()
    {
        FileUtils.DeleteDirectory(Model.PluginPath);
    }

    /// <summary>Starts background descriptor refresh for UI-bound plugin state.</summary>
    private void InitializeWatcher()
    {
        _watcher = new FileSystemWatcher(TargetPath);
        _watcher.Changed += HandleWatcherChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Captures descriptor reload failures rather than throwing outside normal operation handling.</summary>
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
                string? pluginPath = PluginPaths.Instance.FindTargetFile(TargetPath);
                if (pluginPath == null || !string.Equals(args.FullPath, pluginPath, StringComparison.OrdinalIgnoreCase))
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
            ApplicationLogger.Logger.LogError(exception, "Plugin background watcher failed for '{PluginPath}'.", TargetPath);
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
            throw new InvalidOperationException($"Plugin target '{TargetPath}' encountered a background reload failure.", exception);
        }
    }

    /// <summary>Stops plugin and owned-project watching so completed operations release all descriptor lifetimes.</summary>
    public void Dispose()
    {
        Project? hostProject;
        lock (_descriptorLock)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            hostProject = _hostProject;
            _hostProject = null;
        }

        // Parent notifications may access this target, so do not hold its lock while waiting for parent teardown.
        hostProject?.Dispose();
    }
}
