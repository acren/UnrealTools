using System;
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

            ProcessStartInfo startInfo = CreateProcessStartInfo(File, Args);

            return Run(startInfo);
        }

        // Creates the process-start configuration used by plain command-line launch helpers.
        private static ProcessStartInfo CreateProcessStartInfo(string file, string args)
        {
            return new ProcessStartInfo
            {
                Arguments = args,
                FileName = file,
                UseShellExecute = false
            };
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
