using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LocalAutomation.Commands;
using LocalAutomation.Extensions.Unreal.Operations.OperationOptionTypes;
using Microsoft.Extensions.Logging;
using SB.SystemUtilities.Processes;
using SB.UnrealUtilities;

namespace LocalAutomation.Extensions.Unreal.Operations.BaseOperations
{
    // Operation type for running Unreal processes
    public abstract class UnrealProcessOperation<T> : UnrealOperation<T> where T : global::LocalAutomation.Runtime.OperationTarget
    {
        // The owned behavior is reused by custom Unreal launch task bodies that perform preparation before process startup.
        private readonly CommandProcessBehavior _commandProcessBehavior;

        /// <summary>
        /// Composes Unreal process execution while retaining report processing in the Unreal hierarchy.
        /// </summary>
        protected UnrealProcessOperation()
        {
            _commandProcessBehavior = new CommandProcessBehavior(BuildCommand, UnrealCommandProcessPolicy.Instance, observeOutputLine: OnOutputLine, processEnded: OnProcessEnded);
            UseExecutionBehavior(_commandProcessBehavior);
        }

        /// <summary>
        /// Executes the composed command after operation-specific runtime preparation has completed.
        /// </summary>
        protected Task<global::LocalAutomation.Runtime.OperationResult> ExecuteCommandAsync(global::LocalAutomation.Runtime.ExecutionTaskContext context)
        {
            return _commandProcessBehavior.ExecuteAsync(context);
        }

        /// <summary>
        /// Builds the Unreal process command for the validated operation parameters.
        /// </summary>
        protected abstract Command BuildCommand(global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters);

        /// <summary>
        /// Lets derived Unreal process operations inspect raw stdout before classification.
        /// </summary>
        protected virtual void OnOutputLine(string line)
        {
        }

        /// <summary>
        /// Unreal process launches always expose tracing, flag, and automation option groups because shared argument
        /// construction reads all three when building the command line.
        /// </summary>
        protected override System.Collections.Generic.IEnumerable<System.Type> GetDeclaredOptionSetTypes(global::LocalAutomation.Runtime.IOperationTarget target)
        {
            return base.GetDeclaredOptionSetTypes(target)
                .Concat(new[]
                {
                    typeof(InsightsOptions),
                    typeof(FlagOptions),
                    typeof(AutomationOptions)
                });
        }

        /// <summary>Binds persisted launch options and host output allocation to the independent request.</summary>
        protected UnrealLaunchRequest CreateLaunchRequest(global::LocalAutomation.Runtime.ValidatedOperationParameters parameters, bool includeProjectPath = false)
        {
            FlagOptions flags = parameters.GetOptions<FlagOptions>();
            AutomationOptions automation = parameters.GetOptions<AutomationOptions>();
            return new UnrealLaunchRequest
            {
                ProjectDescriptorPath = includeProjectPath && parameters.Target is Targets.Project project ? project.Model.UProjectPath : null,
                TraceChannels = parameters.GetOptions<InsightsOptions>().TraceChannels.ToArray(),
                StompMalloc = flags.StompMalloc,
                WaitForAttach = flags.WaitForAttach,
                NoMessaging = flags.NoMessaging,
                DdcForceMemoryCache = flags.DdcForceMemoryCache,
                Multiprocess = flags.Multiprocess,
                AutomationFilter = automation.RunTests ? automation.TestFilter : null,
                Headless = automation.Headless,
                ReportOutputDirectory = automation.RunTests ? OutputPaths.GetTestReportPath(GetOutputPath(parameters)) : null
            };
        }

        /// <summary>Reads the executed command's report output with the selected engine and maps results to task failure.</summary>
        protected virtual void OnProcessEnded(global::LocalAutomation.Runtime.ExecutionTaskContext context, global::LocalAutomation.Runtime.ValidatedOperationParameters operationParameters, Command command, global::LocalAutomation.Runtime.OperationResult result)
        {
            // Report test results
            AutomationOptions automationOptions = operationParameters.GetOptions<AutomationOptions>();
            if (!result.WasCancelled && automationOptions.RunTests)
            {
                Engine engine = GetRequiredTargetEngineInstall(operationParameters);
                // ReportExportPath can be overridden during finalization; read the directory Unreal actually received.
                Arguments arguments = new();
                arguments.AddRawArgsString(command.Arguments);
                string? reportDirectory = arguments.GetArgument("ReportExportPath")?.Value;
                if (string.IsNullOrWhiteSpace(reportDirectory))
                {
                    throw new InvalidOperationException("Automation command requires a ReportExportPath.");
                }

                string reportFilePath = Path.Combine(reportDirectory, "index.json");
                TestReport? report = TestReport.ReadRequiredReport(reportFilePath, engine);
                if (report == null)
                {
                    context.Logger.LogWarning("Engine version does not support test reports, so results cannot be checked");
                }
                else
                {
                    foreach (Test test in report.Tests)
                    {
                        context.Logger.Log(test.State == TestState.Success ? LogLevel.Information : LogLevel.Error, EnumUtils.GetName(test.State).ToUpperInvariant().PadRight(7) + " - " + test.FullTestPath);
                        foreach (TestEntry entry in test.Entries)
                        {
                            if (entry.Event.Type != TestEventType.Info)
                            {
                                context.Logger.Log(entry.Event.Type == TestEventType.Error ? LogLevel.Error : LogLevel.Warning, "".PadRight(9) + " - " + entry.Event.Message);
                            }
                        }
                    }

                    int testsPassed = report.Tests.Count(t => t.State == TestState.Success);
                    bool allPassed = testsPassed == report.Tests.Count;
                    context.Logger.Log(allPassed ? LogLevel.Information : LogLevel.Error, testsPassed + " of " + report.Tests.Count + " tests passed");

                    if (report.HasFailures)
                    {
                        throw new Exception("Tests failed");
                    }
                }

            }
        }
    }
}
