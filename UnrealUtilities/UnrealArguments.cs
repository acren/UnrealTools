using System;
using System.Collections.Generic;
using SystemUtilities.Processes;

namespace UnrealUtilities
{
    public static class UnrealArguments
    {
        /// <summary>Constructs shared Unreal launch arguments from explicit descriptor, tracing and automation inputs.</summary>
        public static Arguments MakeArguments(UnrealLaunchRequest request)
        {
            Arguments arguments = new();

            if (request.ProjectDescriptorPath != null)
            {
                arguments.SetPath(request.ProjectDescriptorPath);
            }

            arguments.SetFlag("stdout");
            arguments.SetFlag("FullStdOutLogOutput");
            arguments.SetFlag("nologtimes");

            bool useInsights = request.TraceChannels.Count > 0;

            if (useInsights)
            {
                var traceChannels = new List<string>();
                foreach (TraceChannel channel in request.TraceChannels)
                {
                    traceChannels.Add(channel.Key);
                }

                arguments.SetKeyValue("trace", string.Join(",", traceChannels));

                if (traceChannels.Contains("cpu"))
                {
                    arguments.SetFlag("statnamedevents");
                }

                arguments.SetKeyValue("tracehost", "127.0.0.1");
            }

            // Process flags are explicit launch-time Unreal switches and should not be inferred from unrelated options.
            if (request.StompMalloc)
            {
                arguments.SetFlag("stompmalloc");
            }

            if (request.WaitForAttach)
            {
                arguments.SetFlag("waitforattach");
            }

            if (request.NoMessaging)
            {
                arguments.SetFlag("NoMessaging");
            }

            if (request.DdcForceMemoryCache)
            {
                arguments.SetFlag("DDC-ForceMemoryCache");
            }

            if (request.Multiprocess)
            {
                // Multiprocess forwards Unreal's secondary-process marker when the typed launch flag is enabled.
                arguments.SetFlag("Multiprocess");
            }

            if (request.AutomationFilter != null)
            {
                if (string.IsNullOrWhiteSpace(request.ReportOutputDirectory))
                {
                    throw new ArgumentException("Automation requires a report output directory.", nameof(request));
                }

                string execCmds = $"Automation RunTests {request.AutomationFilter};Quit";
                arguments.SetKeyValue("ExecCmds", execCmds);
                arguments.SetKeyPath("ReportExportPath", request.ReportOutputDirectory);
                if (request.Headless)
                {
                    // Run tests as unattended and headless
                    arguments.SetFlag("unattended");
                    arguments.SetFlag("nullrhi");
                    // Disable splash
                    arguments.SetFlag("nosplash");
                    // Disable tutorial to prevent crash from nullrhi
                    arguments.SetFlag("ini:EditorSettings:[/Script/IntroTutorials.EditorTutorialSettings]:StartupTutorial=");
                }
                else
                {
                    arguments.SetFlag("windowed");
                    arguments.SetKeyValue("resx", "640");
                    arguments.SetKeyValue("resy", "360");
                    // Disable background throttle, which will otherwise prevent tests starting if window is in background
                    arguments.SetFlag("ini:EditorSettings:[/Script/UnrealEd.EditorPerformanceSettings]:bThrottleCPUWhenNotForeground=0");
                }
            }

            return arguments;
        }

        /// <summary>Launches the selected editor configuration with optional project and automation arguments.</summary>
        public static Command CreateEditorCommand(Engine engine, BuildConfiguration configuration, UnrealLaunchRequest request)
        {
            return new Command(engine.GetEditorExe(configuration), MakeArguments(request).ToString());
        }

        /// <summary>Runs the editor as a game, retaining explicitly requested automation window dimensions.</summary>
        public static Command CreateStandaloneCommand(Engine engine, BuildConfiguration configuration, UnrealLaunchRequest request)
        {
            Arguments arguments = MakeArguments(request);
            arguments.SetFlag("game");
            arguments.SetFlag("windowed");
            arguments.SetKeyValue("resx", "1920", false);
            arguments.SetKeyValue("resy", "1080", false);
            return new Command(engine.GetEditorExe(configuration), arguments.ToString());
        }

        /// <summary>Launches a packaged executable using the shared tracing and automation request.</summary>
        public static Command CreatePackageCommand(Package package, UnrealLaunchRequest request)
        {
            Arguments arguments = MakeArguments(request);
            arguments.SetFlag("windowed");
            arguments.SetKeyValue("resx", "1920", false);
            arguments.SetKeyValue("resy", "1080", false);
            return new Command(package.ExecutablePath, arguments.ToString());
        }

        /// <summary>Runs the Development editor commandlet executable for unattended project data validation.</summary>
        public static Command CreateDataValidationCommand(Engine engine, UnrealLaunchRequest request)
        {
            Arguments arguments = MakeArguments(request);
            arguments.SetKeyValue("run", "DataValidation");
            arguments.SetFlag("unattended");
            arguments.SetFlag("nop4");
            return new Command(engine.GetEditorCmdExe(BuildConfiguration.Development), arguments.ToString());
        }
    }
}
