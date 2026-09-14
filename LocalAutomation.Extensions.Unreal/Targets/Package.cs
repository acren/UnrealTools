using System;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Newtonsoft.Json;
using SB.UnrealUtilities;
using EngineModel = SB.UnrealUtilities.Engine;
using PackageModel = SB.UnrealUtilities.Package;

namespace LocalAutomation.Extensions.Unreal.Targets;

/// <summary>Provides package runtime identity and owns the staged build's project parent.</summary>
[Target]
public class Package : OperationTarget, IPackageProvider, IEngineInstanceProvider, IDisposable
{
    // Parent creation and disposal share a lock so hierarchy reads cannot leak a watcher during teardown.
    private readonly object _parentLock = new();
    private Project? _hostProject;
    private bool _disposed;

    /// <summary>Gets the executable and staged-build model.</summary>
    [JsonIgnore]
    public PackageModel Model { get; }

    /// <summary>Creates a selectable package from a directory containing its executable.</summary>
    [JsonConstructor]
    public Package(string targetPath) : this(new PackageModel(targetPath))
    {
    }

    /// <summary>Wraps a staged package model at the runtime entry point.</summary>
    public Package(PackageModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        TargetPath = model.TargetPath;
    }

    public override string Name => Model.Name;
    public override bool IsValid => Model.IsValid;
    public EngineModel EngineInstance => Model.EngineInstance;
    public string EngineInstanceName => Model.EngineInstanceName;
    public override IOperationTarget? ParentTarget => HostProject;

    /// <summary>Reuses one owned project target when this package belongs to a staged build.</summary>
    public Project? HostProject
    {
        get
        {
            lock (_parentLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_hostProject == null && Model.HostProjectPath is string projectPath)
                {
                    // Path construction lets the project target wait for its descriptor before creating the model.
                    _hostProject = new Project(projectPath);
                }

                return _hostProject;
            }
        }
    }

    /// <summary>Provides this package directly because its executable already determines its layout.</summary>
    public Package GetProvidedPackage(EngineModel engineContext) => this;

    /// <summary>Packages have executable metadata rather than a reloadable descriptor.</summary>
    public override void LoadDescriptor()
    {
        throw new NotImplementedException();
    }

    /// <summary>Stops the owned project's watcher and prevents hierarchy reads from recreating it.</summary>
    public void Dispose()
    {
        Project? hostProject;
        lock (_parentLock)
        {
            _disposed = true;
            hostProject = _hostProject;
            _hostProject = null;
        }

        // Parent notifications may traverse this hierarchy while the project finishes its pending callback.
        hostProject?.Dispose();
    }
}
