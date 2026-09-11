using System;
using System.Collections.Generic;
using System.IO;
using SystemUtilities.Processes;

namespace UnrealUtilities;

/// <summary>Constructs direct UnrealBuildTool requests and validates their explicit compiler and project inputs.</summary>
public static class UbtArguments
{
    /// <summary>Prepares project-file generation from the actual project descriptor.</summary>
    public static Arguments CreateProjectFilesArguments(Project project)
    {
        Arguments arguments = new();
        arguments.SetFlag("projectfiles");
        arguments.SetKeyPath("project", project.UProjectPath);
        arguments.SetFlag("game");
        arguments.SetFlag("rocket");
        arguments.SetFlag("progress");
        return arguments;
    }

    /// <summary>Resolves source and content-only editor targets and constructs their direct build tuple.</summary>
    public static Arguments CreateEditorTargetArguments(Project project, BuildConfiguration configuration)
    {
        ProjectDescriptor descriptor = project.ProjectDescriptor
            ?? throw new InvalidOperationException("Build Editor Target requires a loaded project descriptor.");
        /* Explicit editor Unreal modules supply the target name. Source projects otherwise append Editor to the
           primary Unreal module; content-only projects use the temporary target named from the project descriptor. */
        ModuleDeclaration? editorModule = descriptor.Modules.Find(module => string.Equals(module.Type, "Editor", StringComparison.OrdinalIgnoreCase));
        string targetName = editorModule != null ? editorModule.Name
            : descriptor.Modules.Count > 0 ? descriptor.Modules[0].Name + "Editor" : project.Name + "Editor";
        Arguments arguments = new();
        arguments.SetArgument(targetName);
        arguments.SetArgument("Win64");
        arguments.SetArgument(configuration.ToString());
        arguments.SetPath(project.UProjectPath);
        return arguments;
    }

    /// <summary>Uses the build identity shared with receipt selection to construct a project target invocation.</summary>
    public static Arguments CreateProjectTargetArguments(Project project, ProjectTargetBuildSpec buildTarget)
    {
        Arguments arguments = new();
        arguments.SetArgument(buildTarget.TargetName);
        arguments.SetArgument(buildTarget.Platform);
        arguments.SetArgument(buildTarget.Configuration.ToString());
        arguments.SetPath(project.UProjectPath);
        return arguments;
    }

    /// <summary>Reports code-plugin, host-project and resolved-engine requirements in validation order.</summary>
    public static string? CheckPluginBuildRequirements(Plugin plugin, Project? hostProject, Engine? engine)
    {
        if (plugin.IsBlueprintOnly)
        {
            return "Build Plugin only supports code plugins";
        }

        if (hostProject == null || !hostProject.IsValid)
        {
            return "Build Plugin requires the plugin to live inside a valid host project";
        }

        return engine == null ? "Build Plugin could not resolve a host project engine install" : null;
    }

    /// <summary>Constructs an in-place plugin build against a valid host project and explicitly selected engine.</summary>
    public static Arguments CreatePluginBuildArguments(Plugin plugin, Project hostProject, Engine engine, BuildConfiguration configuration)
    {
        if (!hostProject.IsValid)
        {
            throw new InvalidOperationException("Build Plugin requires a valid host project before command generation.");
        }

        // UBT expects the editor tuple before the host descriptor and plugin switch.
        Arguments arguments = new();
        arguments.SetArgument(engine.BaseEditorName);
        arguments.SetArgument("Win64");
        arguments.SetArgument(configuration.ToString());
        arguments.SetPath(hostProject.UProjectPath);
        arguments.SetKeyPath("plugin", plugin.UPluginPath);
        return arguments;
    }

    /// <summary>Rejects unsupported language standards and unavailable engine-preferred Clang toolchains.</summary>
    public static string? CheckCompilerRequirements(Engine engine, UbtCompiler compiler, UbtCppStandard cppStandard)
    {
        // UE 5.5 and newer dropped C++17 support, so this override must fail before invoking UBT.
        EngineVersion engineVersion = engine.Version;
        if (cppStandard == UbtCppStandard.Cpp17 && engineVersion >= new EngineVersion(5, 5, 0))
        {
            return $"C++17 is not supported for Unreal Engine {engineVersion.MajorMinorString} or newer";
        }

        return compiler == UbtCompiler.Clang
            ? UbtClangToolchainPreferences.TryGetPreferredToolchain(engine, out _, out _)
            : null;
    }

    /// <summary>Applies direct-UBT overrides and returns the LLVM root to set in the child process environment.</summary>
    public static string? ApplySharedBuildArguments(Arguments arguments, Engine engine, UbtCompiler compiler, UbtCppStandard cppStandard, bool noHotReload)
    {
        string? clangToolchainRoot = null;
        if (noHotReload)
        {
            arguments.SetFlag("NoHotReload");
        }

        // Engine defaults are represented by omitted switches, not explicit enum names.
        if (compiler != UbtCompiler.Default)
        {
            arguments.SetKeyValue("Compiler", compiler.ToString());
        }

        if (compiler == UbtCompiler.Clang)
        {
            string? error = UbtClangToolchainPreferences.TryGetPreferredToolchain(engine, out string compilerVersion, out clangToolchainRoot);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            // Pin UBT to the engine-preferred Clang family so later unsupported families cannot be auto-selected.
            arguments.SetKeyValue("CompilerVersion", compilerVersion);
        }

        if (cppStandard != UbtCppStandard.Default)
        {
            arguments.SetKeyValue("CppStdEngine", cppStandard.ToString());
        }

        return clangToolchainRoot;
    }

    /// <summary>Refreshes the target-info cache consumed by editor startup.</summary>
    public static Arguments CreateProjectTargetQueryArguments(Project project)
    {
        Arguments arguments = new();
        arguments.SetKeyValue("Mode", "QueryTargets");
        arguments.SetKeyPath("Project", project.UProjectPath);
        arguments.SetKeyPath("Output", Path.Combine(project.ProjectPath, "Intermediate", "TargetInfo.json"));
        arguments.SetFlag("IncludeAllTargets");
        arguments.SetFlag("DontIncludeParentAssembly");
        return arguments;
    }

    /// <summary>Yields game then editor cleanup commands, with every build configuration in enum order.</summary>
    public static IEnumerable<Command> CreateCleanCommands(Project project, Engine engine)
    {
        string cleanPath = Path.Combine(engine.GetBuildFolder(), "BatchFiles", "Clean.bat");
        string editorTargetName = project.ProjectDescriptor?.EditorTargetName
            ?? throw new InvalidOperationException("Clean Project requires a loaded project descriptor.");
        foreach (string target in new[] { project.Name, editorTargetName })
        {
            foreach (BuildConfiguration configuration in EnumUtils.GetAll<BuildConfiguration>())
            {
                // Clean.bat receives an always-quoted descriptor and WaitMutex for each individual invocation.
                yield return new Command(cleanPath, $"{target} Win64 {configuration} -project=\"{project.UProjectPath}\" -WaitMutex");
            }
        }
    }
}
