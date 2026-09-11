using System.IO;

namespace UnrealUtilities
{
    public static class OutputPaths
    {
        public static string GetTestReportPath(string OutputPath)
        {
            return Path.Combine(OutputPath, "TestReport");
        }

        public static string GetTestReportFilePath(string OutputPath)
        {
            return Path.Combine(GetTestReportPath(OutputPath), "index.json");
        }
    }
}
