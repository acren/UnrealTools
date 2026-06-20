using System;
using System.IO;
using LocalAutomation.Application;
using LocalAutomation.Core;
using Serilog.Core;
using Serilog.Events;

namespace TestUtilities;

/// <summary>
/// Boots the product's process-wide logging pipeline for tests that exercise application-owned hosts.
/// </summary>
public static class AppFacingTestLoggingBootstrap
{
    private const long TestLaunchLogFileSizeLimitBytes = 1024 * 1024;
    private const int TestLaunchLogRetainedFileCountLimit = 1;

    /// <summary>
    /// Stores one buffered sink so app-facing tests attach shell-style buffered output without depending on UI services.
    /// </summary>
    private static readonly BufferedLogStream BufferedLogStream = new();

    /// <summary>
    /// Stores the buffered sink reused across app-facing test assemblies in one process.
    /// </summary>
    private static readonly ILogEventSink BufferedLogSink = new BufferedTestLogSink(BufferedLogStream);

    /// <summary>
    /// Initializes both the shared test-local MEL factory and the product's process-wide logging pipeline.
    /// </summary>
    public static void Initialize(string loggerCategoryName, string launchLogFilePrefix)
    {
        _ = TestLoggingBootstrap.LoggerFactory;

        ProcessLoggingBootstrap.EnsureShellOutputsInitialized(
            loggerCategoryName: loggerCategoryName,
            applicationLogSink: BufferedLogSink,
            launchLogFilePath: CreateLaunchLogFilePath(launchLogFilePrefix),
            fileSizeLimitBytes: TestLaunchLogFileSizeLimitBytes,
            retainedFileCountLimit: TestLaunchLogRetainedFileCountLimit);
    }

    /// <summary>
    /// Creates one temp launch-log path for the current test host process.
    /// </summary>
    private static string CreateLaunchLogFilePath(string launchLogFilePrefix)
    {
        if (string.IsNullOrWhiteSpace(launchLogFilePrefix))
        {
            throw new ArgumentException("A launch-log file prefix is required.", nameof(launchLogFilePrefix));
        }

        return Path.Combine(
            Path.GetTempPath(),
            $"{launchLogFilePrefix}_{Environment.ProcessId}.log");
    }

    /// <summary>
    /// Mirrors raw Serilog events into a buffered stream so app-facing tests attach a shell-style in-memory sink.
    /// </summary>
    private sealed class BufferedTestLogSink : ILogEventSink
    {
        /// <summary>
        /// Stores the buffered stream that captures attached process output during tests.
        /// </summary>
        private readonly BufferedLogStream _bufferedLogStream;

        /// <summary>
        /// Creates the buffered sink for one shared test stream.
        /// </summary>
        public BufferedTestLogSink(BufferedLogStream bufferedLogStream)
        {
            _bufferedLogStream = bufferedLogStream ?? throw new ArgumentNullException(nameof(bufferedLogStream));
        }

        /// <summary>
        /// Appends one raw event to the buffered test stream.
        /// </summary>
        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            _bufferedLogStream.Add(logEvent);
        }
    }
}
