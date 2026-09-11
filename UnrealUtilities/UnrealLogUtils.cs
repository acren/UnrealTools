using Microsoft.Extensions.Logging;

namespace UnrealUtilities
{
    public static class UnrealLogUtils
    {
        /// <summary>Recognizes Unreal categories, UBT messages and compiler diagnostic severity.</summary>
        public static LogLevel ClassifyOutput(string line)
        {
            string[] split = line.Split(new[] { ": " }, System.StringSplitOptions.None);
            LogLevel level = LogLevel.Information;
            if (split.Length > 1)
            {
                if (split[0] == "ERROR" || split[1] == "Error")
                {
                    level = LogLevel.Error;
                }
                else if (split[1] == "Warning")
                {
                    level = LogLevel.Warning;
                }
            }

            // Compiler diagnostics can have a source-location prefix rather than an Unreal log category.
            if (line.Contains("): error") || line.Contains(" : error ") || line.Contains("): fatal error") || line.Contains(" : fatal error"))
            {
                return LogLevel.Error;
            }

            if (line.Contains("): warning") || line.Contains(" : warning "))
            {
                return LogLevel.Warning;
            }

            return level;
        }

        // Hacky way of checking log follows syntax:
        // [*][int]*
        // If it does, we assume it's a timestamped line
        public static bool IsTimestampedLog(string line)
        {
            if (line.StartsWith("["))
            {
                string[] split = line.Split('[');
                if (split.Length >= 2 && split[1].EndsWith("]") && split[2].Contains("]"))
                {
                    string[] closeSplit = split[2].Split(']');
                    int Result;
                    if (int.TryParse(closeSplit[0], out Result))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Assumes the line actually has a timestamp, otherwise might remove non-timestamp parts
        public static string RemoveTimestamp(string line)
        {
            int secondCloseIndex = line.IndexOf(']', line.IndexOf(']') + 1);
            return line.Remove(0, secondCloseIndex + 1);
        }
    }
}
