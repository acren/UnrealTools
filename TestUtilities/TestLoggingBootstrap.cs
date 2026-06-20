using System;
using Microsoft.Extensions.Logging;

namespace TestUtilities;

/// <summary>
/// Builds one shared MEL logger factory for test assemblies without taking ownership of the product's global logging
/// bootstrap.
/// </summary>
public static class TestLoggingBootstrap
{
    private static readonly object SyncRoot = new();
    private static ILoggerFactory? _loggerFactory;
    private static bool _isInitialized;

    /// <summary>
    /// Gets the shared test logger factory, initializing it on first access.
    /// </summary>
    public static ILoggerFactory LoggerFactory
    {
        get
        {
            EnsureInitialized();
            return _loggerFactory!;
        }
    }

    /// <summary>
    /// Creates the shared test logger factory once so tests can request ordinary MEL loggers without mutating or
    /// depending on application-layer bootstrap ownership.
    /// </summary>
    private static void EnsureInitialized()
    {
        lock (SyncRoot)
        {
            if (_isInitialized)
            {
                return;
            }

            _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddConsole();
            });

            _isInitialized = true;
        }
    }
}
