using System;
using System.Collections.Generic;
using System.Reflection;
using LocalAutomation.Core;
using LocalAutomation.Extensions.Abstractions;
using SB.SystemUtilities.Processes;
using SB.UnrealUtilities;
using Project = LocalAutomation.Extensions.Unreal.Targets.Project;
using RuntimeTarget = global::LocalAutomation.Runtime.IOperationTarget;

namespace LocalAutomation.Extensions.Unreal;

/// <summary>
/// Registers the current Unreal-focused targets and operations as a compile-time LocalAutomation extension module.
/// </summary>
public sealed class UnrealExtensionModule : IExtensionModule
{
    /// <summary>
    /// Gets the stable identifier for the Unreal extension.
    /// </summary>
    public string Id => "unreal";

    /// <summary>
    /// Gets the display name used for diagnostics and future UI surfaces.
    /// </summary>
    public string DisplayName => "Unreal Engine";

    /// <summary>
    /// Supplies the assembly that owns the Unreal extension's runtime targets and operation descriptors.
    /// </summary>
    public IEnumerable<Assembly> GetDescriptorAssemblies()
    {
        return new[] { typeof(UnrealExtensionModule).Assembly };
    }

    /// <summary>
    /// Registers Unreal target descriptors, operations, and target creation logic.
    /// </summary>
    public void Register(IExtensionRegistry registry)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        registry.RegisterTargetFactory(new UnrealPathTargetFactory());
        registry.RegisterSettingValueConverter(new EngineVersionListOptionValueConverter());
        registry.RegisterSettingValueConverter(new TraceChannelListOptionValueConverter());
        RegisterContextActions(registry);
    }

    /// <summary>
    /// Registers the first parity-focused set of target actions used by the Avalonia shell.
    /// </summary>
    private static void RegisterContextActions(IExtensionRegistry registry)
    {
        registry.RegisterContextAction(new ContextActionDescriptor(
            id: new ContextActionId("unreal.target.open-directory"),
            displayName: "Open Directory",
            targetType: typeof(RuntimeTarget),
            execute: target => RunProcess.OpenDirectory(((RuntimeTarget)target).TargetDirectory)));

        registry.RegisterContextAction(new ContextActionDescriptor(
            id: new ContextActionId("unreal.target.open-output"),
            displayName: "Open Output",
            targetType: typeof(RuntimeTarget),
            execute: target => RunProcess.OpenDirectory(((RuntimeTarget)target).OutputDirectory)));

        registry.RegisterContextAction(new ContextActionDescriptor(
            id: new ContextActionId("unreal.project.open-staged-build"),
            displayName: "Open Staged Build",
            targetType: typeof(Project),
            execute: target =>
            {
                Project project = (Project)target;
                Engine? engine = project.EngineInstance;
                if (engine == null)
                {
                    throw new InvalidOperationException($"Project '{project.DisplayName}' does not currently resolve to an engine install.");
                }

                RunProcess.OpenDirectory(project.Model.GetStagedBuildWindowsPath(engine));
            },
            canExecute: target => ((Project)target).EngineInstance != null));
    }
}
