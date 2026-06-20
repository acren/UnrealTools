using System;
using LocalAutomation.Core;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LocalAutomation.Application;

/// <summary>
/// Owns process-wide Serilog logger construction, assignment, and shared process-output composition for supported hosts.
/// </summary>
public static class ProcessLoggingBootstrap
{
    private static readonly object SyncRoot = new();
    private static ILoggerFactory? _loggerFactory;
    private static Serilog.ILogger? _sharedProcessOutputLogger;

    /// <summary>
    /// Gets the shared process-output logger used by execution sessions after supported hosts initialize process logging.
    /// </summary>
    public static Serilog.ILogger GetSharedProcessOutputLogger()
    {
        lock (SyncRoot)
        {
            return _sharedProcessOutputLogger
                ?? throw new InvalidOperationException("Process logging must be initialized by the shell or app-facing test bootstrap before starting an execution session.");
        }
    }

    /// <summary>
    /// Ensures the process logger uses the shell-owned buffered-output and launch-log sinks for supported hosts.
    /// </summary>
    public static void EnsureShellOutputsInitialized(
        string loggerCategoryName,
        Serilog.Core.ILogEventSink applicationLogSink,
        string launchLogFilePath,
        long fileSizeLimitBytes,
        int retainedFileCountLimit)
    {
        ArgumentNullException.ThrowIfNull(applicationLogSink);
        if (string.IsNullOrWhiteSpace(launchLogFilePath))
        {
            throw new ArgumentException("Launch log file path is required.", nameof(launchLogFilePath));
        }

        if (fileSizeLimitBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeLimitBytes), "Launch log file size limit must be positive.");
        }

        if (retainedFileCountLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFileCountLimit), "Launch log file retention must be positive.");
        }

        if (string.IsNullOrWhiteSpace(loggerCategoryName))
        {
            throw new ArgumentException("Logger category name is required.", nameof(loggerCategoryName));
        }

        lock (SyncRoot)
        {
            if (_loggerFactory == null)
            {
                Serilog.ILogger sharedProcessOutputLogger = CreateSharedProcessOutputLogger(
                    applicationLogSink,
                    launchLogFilePath,
                    fileSizeLimitBytes,
                    retainedFileCountLimit);
                CreateProcessLogger(sharedProcessOutputLogger);
                _sharedProcessOutputLogger = sharedProcessOutputLogger;
            }

            // Keep the public facade category aligned with the supported host that performed bootstrap.
            ILoggerFactory loggerFactory = _loggerFactory ?? throw new InvalidOperationException("Process logger factory was not initialized.");
            ApplicationLogger.Logger = loggerFactory.CreateLogger(loggerCategoryName);
        }
    }

    /// <summary>
    /// Disposes the process logger bridge and flushes any buffered Serilog output.
    /// </summary>
    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            _loggerFactory?.Dispose();
            _loggerFactory = null;
            (_sharedProcessOutputLogger as IDisposable)?.Dispose();
            _sharedProcessOutputLogger = null;
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Creates the shared process-output logger used by both process-wide MEL logging and session fanout.
    /// </summary>
    private static Serilog.ILogger CreateSharedProcessOutputLogger(
        Serilog.Core.ILogEventSink applicationLogSink,
        string launchLogFilePath,
        long fileSizeLimitBytes,
        int retainedFileCountLimit)
    {
        ArgumentNullException.ThrowIfNull(applicationLogSink);

        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(applicationLogSink)
            /* Keep launch-log threshold policy inside Serilog's native pipeline so file filtering stays a sink concern
               instead of a custom adapter concern. */
            .WriteTo.Logger(configureLogger: logger => logger
                .MinimumLevel.Verbose()
                .Filter.ByIncludingOnly(logEvent => ApplicationLogThresholdSettings.AllowsFileOutput(logEvent.Level))
                .WriteTo.File(
                    formatter: UnifiedLogTextFormatting.Formatter,
                    path: launchLogFilePath,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: fileSizeLimitBytes,
                    retainedFileCountLimit: retainedFileCountLimit,
                    shared: true))
            .CreateLogger();
    }

    /// <summary>
    /// Creates the single process-wide MEL bridge over the shared process-output logger.
    /// </summary>
    private static void CreateProcessLogger(Serilog.ILogger sharedProcessOutputLogger)
    {
        ArgumentNullException.ThrowIfNull(sharedProcessOutputLogger);

        _loggerFactory?.Dispose();
        Log.CloseAndFlush();

        Log.Logger = sharedProcessOutputLogger;
        _loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, dispose: false));
    }
}
