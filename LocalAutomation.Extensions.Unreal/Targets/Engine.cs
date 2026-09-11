using System;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Newtonsoft.Json;
using UnrealUtilities;
using EngineModel = UnrealUtilities.Engine;

namespace LocalAutomation.Extensions.Unreal.Targets;

/// <summary>Provides runtime engine identity for the installation model.</summary>
[Target]
public class Engine : OperationTarget, IEngineInstanceProvider
{
    /// <summary>Gets the installation model used by engine algorithms.</summary>
    [JsonIgnore]
    public EngineModel Model { get; }

    /// <summary>Gets or sets the installation key used by engine selection.</summary>
    [JsonProperty]
    public string Key
    {
        get => Model.Key;
        set => Model.Key = value;
    }

    /// <summary>Creates a selectable installation from its directory.</summary>
    [JsonConstructor]
    public Engine(string targetPath) : this(new EngineModel(targetPath))
    {
    }

    /// <summary>Wraps a discovered installation, including its engine-selection key.</summary>
    public Engine(EngineModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        TargetPath = model.TargetPath;
    }

    public override string Name => Model.Name;
    public override string DisplayName => Model.DisplayName;
    public override bool IsValid => Model.IsValid;
    public EngineModel EngineInstance => Model;

    /// <summary>Engine version data is read on demand; there is no cached descriptor to reload.</summary>
    public override void LoadDescriptor()
    {
        throw new NotImplementedException();
    }

}
