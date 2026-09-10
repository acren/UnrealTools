using System;
using LocalAutomation.Core.IO;
using LocalAutomation.Extensions.Abstractions;
using LocalAutomation.Runtime;
using Newtonsoft.Json;
using UnrealAutomationCommon.Unreal;
using EngineModel = UnrealAutomationCommon.Unreal.Engine;
using PluginModel = UnrealAutomationCommon.Unreal.Plugin;

namespace LocalAutomation.Extensions.Unreal.Targets;

/// <summary>Provides runtime engine identity and installed-plugin filesystem management.</summary>
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

    /// <summary>Removes an installed plugin's files from the engine installation.</summary>
    public void UninstallPlugin(string pluginName)
    {
        PluginModel plugin = Model.FindInstalledPlugin(pluginName)
            ?? throw new InvalidOperationException("Could not find plugin in installed plugins");
        /* Uninstallation deletes plugin files. A plugin installed via Epic Launcher may remain registered there
           because this filesystem operation does not change the launcher's installation records. */
        FileUtils.DeleteDirectory(plugin.PluginPath);
    }
}
