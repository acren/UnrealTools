using System;
using System.Collections.Generic;

namespace UnrealUtilities
{
    public static class UATArguments
    {
        /// <summary>Constructs one project UAT invocation from explicit phase, path and build choices.</summary>
        public static Arguments CreateBuildCookRunArguments(Project project, Engine engine, BuildCookRunProjectRequest request, bool noHotReload)
        {
            Arguments arguments = new();
            arguments.SetArgument("BuildCookRun");
            arguments.SetKeyPath("project", project.UProjectPath);
            ApplyPhaseArguments(arguments, request);

            // BuildCookRun forwards UBT-specific switches only when compilation is enabled.
            if (request.HasPhase(BuildCookRunProjectPhases.Build) && noHotReload)
            {
                arguments.SetKeyValue("ubtargs", "-NoHotReload");
            }

            string configuration = request.Configuration.ToString();
            arguments.SetKeyValue("clientconfig", configuration);
            arguments.SetKeyValue("serverconfig", configuration);
            if (request.NoDebugInfo)
            {
                arguments.SetFlag("NoDebugInfo");
            }

            if (!string.IsNullOrWhiteSpace(request.ArchiveDirectory))
            {
                arguments.SetFlag("archive");
                arguments.SetKeyPath("archivedirectory", request.ArchiveDirectory!);
            }

            // Explicit staging and cook roots allow each caller to isolate its current-run package outputs.
            if (!string.IsNullOrWhiteSpace(request.StagingDirectory))
            {
                arguments.SetKeyPath("stagingdirectory", request.StagingDirectory!);
            }

            if (!string.IsNullOrWhiteSpace(request.CookOutputDirectory))
            {
                arguments.SetKeyPath("CookOutputDir", request.CookOutputDirectory!);
            }

            if (!string.IsNullOrWhiteSpace(request.UnrealExePath))
            {
                arguments.SetKeyPath("unrealexe", request.UnrealExePath!);
            }

            if (!string.IsNullOrWhiteSpace(request.AdditionalCookerOptions))
            {
                arguments.SetKeyValue("additionalcookeroptions", request.AdditionalCookerOptions!);
            }

            arguments.ApplyCommonUATArguments(engine);
            return arguments;
        }

        /// <summary>Creates a build-only editor request, rejecting configurations UAT cannot honor.</summary>
        public static BuildCookRunProjectRequest CreateEditorBuildRequest(BuildConfiguration configuration)
        {
            // UAT forces Development internally; accepting another configuration would misrepresent the build.
            if (configuration != BuildConfiguration.Development)
            {
                throw new InvalidOperationException("Configuration is not supported");
            }

            return new BuildCookRunProjectRequest(BuildCookRunProjectPhases.Build, configuration: configuration);
        }

        /// <summary>Creates a full packaging request with optional compilation, archive output and cooker overrides.</summary>
        public static BuildCookRunProjectRequest CreateProjectPackageRequest(Engine engine, BuildConfiguration configuration, bool build, bool noDebugInfo, string? archiveDirectory, BuildConfiguration cookerConfiguration, bool waitForAttach)
        {
            BuildCookRunProjectPhases phases = BuildCookRunProjectPhases.Cook
                | BuildCookRunProjectPhases.Stage | BuildCookRunProjectPhases.Pak | BuildCookRunProjectPhases.Package;
            if (build)
            {
                phases |= BuildCookRunProjectPhases.Build;
            }

            // The Development cooker uses UAT's default executable selection.
            return new BuildCookRunProjectRequest(phases, configuration: configuration, noDebugInfo: noDebugInfo,
                archiveDirectory: archiveDirectory,
                unrealExePath: cookerConfiguration != BuildConfiguration.Development ? engine.GetEditorCmdExe(cookerConfiguration) : null,
                additionalCookerOptions: waitForAttach ? "-waitforattach" : null);
        }

        /// <summary>Constructs distributable plugin packaging for the supplied ordered platform selection.</summary>
        public static Arguments CreateBuildPluginArguments(Plugin plugin, Engine engine, string packagePath, IEnumerable<string> targetPlatforms, bool strictIncludes)
        {
            Arguments arguments = new();
            arguments.SetArgument("BuildPlugin");
            arguments.SetKeyPath("Plugin", plugin.UPluginPath);
            arguments.SetKeyPath("Package", packagePath);
            arguments.SetFlag("Rocket");
            // UAT BuildPlugin already forwards NoHotReload to each UBT invocation it owns.
            arguments.SetKeyValue("TargetPlatforms", string.Join('+', targetPlatforms));
            if (strictIncludes)
            {
                arguments.SetFlag("StrictIncludes");
            }

            arguments.ApplyCommonUATArguments(engine);
            return arguments;
        }

        /// <summary>Emits enabled phase flags in UAT command order.</summary>
        private static void ApplyPhaseArguments(Arguments arguments, BuildCookRunProjectRequest request)
        {
            if (request.HasPhase(BuildCookRunProjectPhases.Build))
            {
                arguments.SetFlag("build");
            }

            if (request.HasPhase(BuildCookRunProjectPhases.Cook))
            {
                arguments.SetFlag("cook");
            }

            if (request.HasPhase(BuildCookRunProjectPhases.Stage))
            {
                arguments.SetFlag("stage");
            }

            if (request.HasPhase(BuildCookRunProjectPhases.Pak))
            {
                arguments.SetFlag("pak");
            }

            if (request.HasPhase(BuildCookRunProjectPhases.Package))
            {
                arguments.SetFlag("package");
            }
        }

        public static void ApplyCommonUATArguments(this Arguments arguments, Engine engine)
        {
            if (engine.Version != null && engine.Version >= new EngineVersion(5, 0))
            {
                // Prevent turnkey errors in UE5
                arguments.SetFlag("noturnkeyvariables");
            }
        }
    }
}
