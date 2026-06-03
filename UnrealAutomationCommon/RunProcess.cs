using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnrealAutomationCommon.Unreal;

namespace UnrealAutomationCommon
{
    public static class RunProcess
    {
        public static Process Run(ProcessStartInfo StartInfo)
        {
            Process process = new() { StartInfo = StartInfo };
            process.Start();
            return process;
        }

        public static Process Run(string File, string Args)
        {
            if (File == null)
            {
                throw new ArgumentNullException(nameof(File));
            }

            ProcessStartInfo startInfo = CreateProcessStartInfo(File, Args, Array.Empty<KeyValuePair<string, string>>());

            return Run(startInfo);
        }

        // Runs a fully composed runtime command, including any child-process environment overrides.
        public static Process Run(LocalAutomation.Runtime.Command command)
        {
            ProcessStartInfo startInfo = CreateProcessStartInfo(command.File, command.Arguments, command.EnvironmentVariables);
            return Run(startInfo);
        }

        // Creates the process-start configuration used by both plain and command-backed launch helpers.
        private static ProcessStartInfo CreateProcessStartInfo(string file, string args, IEnumerable<KeyValuePair<string, string>> environmentVariables)
        {
            ProcessStartInfo startInfo = new()
            {
                Arguments = args,
                FileName = file,
                UseShellExecute = false
            };

            foreach (KeyValuePair<string, string> environmentVariable in environmentVariables)
            {
                if (string.IsNullOrWhiteSpace(environmentVariable.Key))
                {
                    throw new InvalidOperationException("Command environment variable names must not be blank.");
                }

                startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
            }

            return startInfo;
        }

        public static Process RunAndWait(string File, string Args)
        {
            Process process = Run(File, Args);
            process.WaitForExit();
            return process;
        }

        public static void Run(string File, UnrealArguments Args)
        {
            /* Unreal argument builders are expected to format themselves as strings, but null-safe fallback keeps this
               thin wrapper from passing a null argument string into the process-launch overload. */
            Run(File, Args?.ToString() ?? string.Empty);
        }

        public static void OpenDirectory(string DirectoryPath)
        {
            Directory.CreateDirectory(DirectoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = DirectoryPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
    }
}
