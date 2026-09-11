#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SystemUtilities.Processes
{
    /// <summary>
    /// Executes a command with streaming logs, child-only environment overrides and process-tree cancellation.
    /// </summary>
    public static class ProcessExecutor
    {
        // System-level critical errors must return to unattended command trees instead of opening modal prompts.
        private const uint SemFailCriticalErrors = 0x0001;

        // Unhandled child-process faults must terminate so callers can observe their nonzero exit codes.
        private const uint SemNoGpFaultErrorBox = 0x0002;

        /// <summary>
        /// Configures the Windows process policy inherited by every command descendant.
        /// </summary>
        public static void ConfigureUnattendedWindowsErrors()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // Preserve host policies while preventing descendant failures from waiting for interactive Windows UI.
            uint currentErrorMode = GetErrorMode();
            SetErrorMode(currentErrorMode | SemFailCriticalErrors | SemNoGpFaultErrorBox);
        }

        /// <summary>
        /// Reads the process-wide Windows error policy so unrelated flags remain enabled.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern uint GetErrorMode();

        /// <summary>
        /// Sets the process-wide Windows error policy inherited by child processes.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint errorMode);

        /// <summary>
        /// Returns observed process facts after exit; startup failures throw and termination failures are logged.
        /// </summary>
        public static async Task<ProcessResult> ExecuteAsync(
            Command command,
            CommandProcessPolicy policy,
            ILogger logger,
            CancellationToken cancellationToken,
            Action<string>? onOutputLine = null)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            CommandProcessState state = new(Path.GetFileName(command.File));

            logger.LogInformation("Running command: " + command);
            if (!File.Exists(command.File))
            {
                throw new FileNotFoundException("Process command file was not found.", command.File);
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = command.File,
                Arguments = command.Arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ApplyCommandEnvironment(startInfo, command.EnvironmentVariables);

            state.Process = new Process { StartInfo = startInfo };
            state.Process.EnableRaisingEvents = true;
            state.Process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    HandleLogLine(logger, args.Data, policy, onOutputLine);
                }
            };
            state.Process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    logger.LogError(args.Data);
                }
            };
            state.Process.Start();
            state.Process.BeginOutputReadLine();
            state.Process.BeginErrorReadLine();

            state.ProcessName = state.Process.ProcessName;
            logger.LogInformation("Launched process '" + state.FileAndProcess + "'");
            TaskCompletionSource<int> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Process.Exited += (_, _) =>
            {
                logger.LogDebug($"Process '{state.FileAndProcess}' exited");
                tcs.TrySetResult(0);
            };
            if (state.Process.HasExited)
            {
                // Very short-lived commands can exit before the handler is attached even with raising enabled.
                tcs.TrySetResult(0);
            }

            /* Register the live process-cancellation callback only after launch so terminate requests can report the
               concrete process identity they attempted to stop. */
            CancellationTokenRegistration registration = cancellationToken.Register(() => TryTerminateProcessForCancellation(logger, state));

            await tcs.Task.ConfigureAwait(false);

            // Dispose the registration so the cancellation callback does not keep this task state alive after exit.
            await registration.DisposeAsync().ConfigureAwait(false);

            return HandleProcessEnded(logger, state.Process, state.FileAndProcess, state.WasCancelled);
        }

        /// <summary>
        /// Applies environment overrides to the child process without mutating the parent process environment.
        /// </summary>
        private static void ApplyCommandEnvironment(ProcessStartInfo startInfo, IEnumerable<KeyValuePair<string, string>> environmentVariables)
        {
            foreach (KeyValuePair<string, string> environmentVariable in environmentVariables)
            {
                if (string.IsNullOrWhiteSpace(environmentVariable.Key))
                {
                    throw new InvalidOperationException("Command environment variable names must not be blank.");
                }

                startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
            }
        }

        /// <summary>
        /// Classifies one process output line and lets callers inspect stdout without storing the full process log.
        /// </summary>
        private static void HandleLogLine(ILogger logger, string line, CommandProcessPolicy policy, Action<string>? onOutputLine)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            // Observers receive raw stdout before policy classification so tool-specific parsers see every emitted line.
            onOutputLine?.Invoke(line);
            logger.Log(policy.ClassifyOutput(line), line);
        }

        /// <summary>
        /// Reports the observed exit code and cancellation state and logs the final process status.
        /// </summary>
        private static ProcessResult HandleProcessEnded(ILogger logger, Process process, string fileAndProcess, bool wasCancelled)
        {
            ProcessResult result = new(process.ExitCode, wasCancelled);
            LogLevel exitLevel = wasCancelled
                ? LogLevel.Warning
                : result.ExitCode == 0
                    ? LogLevel.Information
                    : LogLevel.Error;
            string exitLabel = wasCancelled
                ? "cancelled"
                : result.ExitCode == 0
                    ? "succeeded"
                    : "failed";
            logger.Log(exitLevel, "Process '" + fileAndProcess + "' " + exitLabel + " with code " + result.ExitCode);
            return result;
        }

        /// <summary>
        /// Attempts to terminate the active process tree once when cancellation reaches this command.
        /// </summary>
        private static void TryTerminateProcessForCancellation(
            ILogger logger,
            CommandProcessState state)
        {
            state.WasCancelled = true;
            if (Interlocked.Exchange(ref state.CancellationTerminationRequested, 1) != 0)
            {
                logger.LogDebug("Cancellation termination for process '{FileAndProcess}' was already requested.", state.FileAndProcess);
                return;
            }

            Process? process = state.Process;
            if (process == null)
            {
                logger.LogError("Cancellation reached command '{FileAndProcess}' before a process instance was available. No termination attempt could be made.", state.FileAndProcess);
                return;
            }

            int? processId = TryGetProcessId(process);
            try
            {
                if (process.HasExited)
                {
                    logger.LogInformation("Cancellation reached process '{FileAndProcess}'{ProcessIdSuffix}, but it had already exited.", state.FileAndProcess, FormatProcessIdSuffix(processId));
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                logger.LogInformation("Cancellation reached process '{FileAndProcess}'{ProcessIdSuffix}, but it had already exited.", state.FileAndProcess, FormatProcessIdSuffix(processId));
                return;
            }

            logger.LogWarning("Attempting to terminate process '{FileAndProcess}'{ProcessIdSuffix} because cancellation was requested.", state.FileAndProcess, FormatProcessIdSuffix(processId));
            try
            {
                process.Kill(true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Termination attempt for process '{FileAndProcess}'{ProcessIdSuffix} threw an exception.", state.FileAndProcess, FormatProcessIdSuffix(processId));
                return;
            }

            try
            {
                if (process.WaitForExit(2000))
                {
                    logger.LogInformation("Cancellation terminated process '{FileAndProcess}'{ProcessIdSuffix}.", state.FileAndProcess, FormatProcessIdSuffix(processId));
                    return;
                }

                logger.LogError("Cancellation attempted to terminate process '{FileAndProcess}'{ProcessIdSuffix}, but it is still running after 2000 ms.", state.FileAndProcess, FormatProcessIdSuffix(processId));
            }
            catch (InvalidOperationException)
            {
                logger.LogInformation("Cancellation terminated process '{FileAndProcess}'{ProcessIdSuffix}.", state.FileAndProcess, FormatProcessIdSuffix(processId));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Termination verification for process '{FileAndProcess}'{ProcessIdSuffix} failed after the kill attempt.", state.FileAndProcess, FormatProcessIdSuffix(processId));
            }
        }

        /// <summary>
        /// Holds mutable process state shared between the running task and its cancellation callback.
        /// </summary>
        private sealed class CommandProcessState(string? fileName)
        {
            /// <summary>
            /// Gets the command file leaf name used in process diagnostics.
            /// </summary>
            internal string? FileName { get; } = fileName;

            /// <summary>
            /// Gets or sets the live process instance after the command has launched.
            /// </summary>
            internal Process? Process { get; set; }

            /// <summary>
            /// Gets or sets the OS process name after launch so logs can identify the executable.
            /// </summary>
            internal string? ProcessName { get; set; }

            /// <summary>
            /// Gets or sets whether cancellation requested process termination before exit was observed.
            /// </summary>
            internal bool WasCancelled { get; set; }

            /// <summary>
            /// Stores whether the cancellation callback has already attempted process termination.
            /// </summary>
            internal int CancellationTerminationRequested;

            /// <summary>
            /// Gets a readable process identity even before launch fills in the final process name.
            /// </summary>
            internal string FileAndProcess => $"{FileName ?? "unknown-file"}:{ProcessName ?? "unknown-process"}";
        }

        /// <summary>
        /// Reads the current process identifier for diagnostics without failing when the process has already exited.
        /// </summary>
        private static int? TryGetProcessId(Process process)
        {
            try
            {
                return process.Id;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Formats the optional process identifier for cancellation logs.
        /// </summary>
        private static string FormatProcessIdSuffix(int? processId)
        {
            return processId.HasValue ? $" (pid {processId.Value})" : string.Empty;
        }
    }
}
