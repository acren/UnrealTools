using System;
using System.Collections.Generic;
using SystemUtilities.Formatting;

namespace SystemUtilities.Processes;

/// <summary>
/// Represents a fully constructed command line that can be previewed or executed.
/// </summary>
public class Command
{
    /// <summary>
    /// Creates a command from a file path and raw argument string.
    /// </summary>
    public Command(string file, string arguments)
    {
        File = file;
        Arguments = arguments;
    }

    /// <summary>
    /// Gets or sets the executable or script path to run.
    /// </summary>
    public string File { get; set; }

    /// <summary>
    /// Gets or sets the fully composed argument string for the command.
    /// </summary>
    public string Arguments { get; set; }

    /// <summary>
    /// Gets command-specific environment variables applied only to the launched child process.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Formats the command for display in logs, previews, and diagnostics.
    /// </summary>
    public override string ToString()
    {
        string commandText = CommandLineFormatting.FormatCommand(File, Arguments);
        string environmentPrefix = string.Empty;
        foreach (KeyValuePair<string, string> environmentVariable in EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(environmentVariable.Key))
            {
                throw new InvalidOperationException("Command environment variable names must not be blank.");
            }

            // Command previews use cmd.exe syntax so copied Build.bat commands preserve process-scoped overrides.
            if (!string.IsNullOrEmpty(environmentPrefix))
            {
                environmentPrefix += " && ";
            }

            environmentPrefix += $"set \"{environmentVariable.Key}={EscapeCommandValue(environmentVariable.Value)}\"";
        }

        return string.IsNullOrEmpty(environmentPrefix)
            ? commandText
            : $"{environmentPrefix} && {commandText}";
    }

    /// <summary>
    /// Escapes characters that would otherwise terminate the quoted cmd.exe assignment used in command previews.
    /// </summary>
    private static string EscapeCommandValue(string value)
    {
        return value.Replace("\"", "^\"", StringComparison.Ordinal);
    }
}
