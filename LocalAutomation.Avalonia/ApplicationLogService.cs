using System;
using System.Threading.Tasks;
using LocalAutomation.Application;
using LocalAutomation.Core;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace LocalAutomation.Avalonia;

/// <summary>
/// Owns the shared shell's process-wide Serilog-backed logging pipeline and UI-facing log stream.
/// </summary>
public static class ApplicationLogService
{
    private const int MaxLaunchLogFiles = 20;
    private const int MaxLogFileSizeBytes = 10 * 1024 * 1024;
    private const int MaxRollingLaunchLogFiles = 3;

    private static bool _isInitialized;

    /// <summary>
    /// Gets the shared in-memory log stream rendered by the shell.
    /// </summary>
    public static BufferedLogStream LogStream { get; } = new();

    /// <summary>
    /// Initializes the global application logger bridge and hooks process-wide exception reporting once per run.
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        string launchLogFilePath = CreateLaunchLogFilePath();
        ProcessLoggingBootstrap.EnsureShellOutputsInitialized(
            loggerCategoryName: App.ShellIdentity.LoggerCategoryName,
            applicationLogSink: new ApplicationLogBufferSink(LogStream),
            launchLogFilePath: launchLogFilePath,
            fileSizeLimitBytes: MaxLogFileSizeBytes,
            retainedFileCountLimit: MaxRollingLaunchLogFiles);

        // Capture exceptions that escape normal async or UI flows so the output panel still shows the failure details.
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;

        ApplicationLogger.Logger.LogInformation("Writing launch log to {LaunchLogFilePath}", launchLogFilePath);
        ApplicationLogger.Logger.LogInformation("Shell logging initialized.");
    }

    /// <summary>
    /// Flushes the Serilog pipeline when the shell closes so the latest crash details land on disk.
    /// </summary>
    public static void Shutdown()
    {
        ProcessLoggingBootstrap.Shutdown();
    }

    /// <summary>
    /// Logs a startup failure that happens while the desktop lifetime is being created.
    /// </summary>
    public static void LogStartupException(Exception exception)
    {
        ApplicationLogger.Logger.LogCritical(exception, "Shell failed during startup.");
    }

    /// <summary>
    /// Writes an informational message through the shared application logger bridge.
    /// </summary>
    public static void LogInformation(string message)
    {
        ApplicationLogger.Logger.LogInformation(message);
    }

    /// <summary>
    /// Writes an error entry with exception details through the shared application logger bridge.
    /// </summary>
    public static void LogError(Exception exception, string messageTemplate, params object[] args)
    {
        ApplicationLogger.Logger.LogError(exception, messageTemplate, args);
    }

    /// <summary>
    /// Logs process-wide unhandled exceptions through the shared output stream.
    /// </summary>
    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            ApplicationLogger.Logger.LogCritical(exception, "Unhandled application exception.");
            return;
        }

        ApplicationLogger.Logger.LogCritical("Unhandled non-exception application failure: {ExceptionObject}", args.ExceptionObject);
    }

    /// <summary>
    /// Logs unobserved task exceptions and marks them observed so the runtime does not hide the failure details.
    /// </summary>
    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        ApplicationLogger.Logger.LogCritical(args.Exception, "Unobserved task exception.");
        args.SetObserved();
    }

    /// <summary>
    /// Creates the next launch-log file path after clearing old retained launch logs.
    /// </summary>
    private static string CreateLaunchLogFilePath()
    {
        LoggingPaths.CleanupOldLaunchLogs(MaxLaunchLogFiles);
        return LoggingPaths.CreateLaunchLogFilePath();
    }

}
