using System;
using System.IO;
using LocalAutomation.Core;
using LocalAutomation.Runtime;
using Microsoft.Extensions.Logging;

namespace LocalAutomation.Application;

/// <summary>
/// Persists one execution session's buffered log stream to its own disk file for the lifetime of that session run.
/// </summary>
internal sealed class ExecutionSessionLogWriter : IDisposable
{
    // Serializes file writes and disposal so concurrent task output never writes through a closing stream.
    private readonly object _syncRoot = new();
    // Holds the session whose in-memory stream is mirrored to this file sink.
    private readonly ExecutionSession _session;
    // Owns the active file handle for this session's persisted execution output.
    private readonly StreamWriter _writer;
    // Tracks whether disposal has closed the writer and removed the stream subscription.
    private bool _isDisposed;
    // Prevents repeated write attempts and recursive failure logs after the first file sink failure.
    private bool _isDisabled;

    /// <summary>
    /// Creates a file-backed writer and subscribes it to the provided session stream.
    /// </summary>
    private ExecutionSessionLogWriter(ExecutionSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        FilePath = session.LogFilePath ?? throw new InvalidOperationException("Session log file path is not configured.");

        // Create the session-log directory lazily so hosts that never execute operations never touch the filesystem.
        string logDirectory = Path.GetDirectoryName(FilePath) ?? throw new InvalidOperationException("Session log file path must include a directory.");
        Directory.CreateDirectory(logDirectory);
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };

        _session.LogStream.EntryAdded += HandleEntryAdded;
    }

    /// <summary>
    /// Gets the absolute file path receiving this session's persisted output.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Attempts to attach a file writer to the session, reporting infrastructure failures without blocking execution.
    /// </summary>
    public static ExecutionSessionLogWriter? TryAttach(ExecutionSession session)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (string.IsNullOrWhiteSpace(session.LogFilePath))
        {
            return null;
        }

        try
        {
            return new ExecutionSessionLogWriter(session);
        }
        catch (Exception ex) when (IsLogFileInfrastructureException(ex))
        {
            session.Logger.LogError(ex, "Failed to create session log file '{SessionLogFilePath}'. Session output will remain available in memory only.", session.LogFilePath);
            TryLogApplicationInfrastructureFailure(ex, "Failed to create session log file '{SessionLogFilePath}'.", session.LogFilePath);
            return null;
        }
    }

    /// <summary>
    /// Stops listening for session log entries and closes the file handle.
    /// </summary>
    public void Dispose()
    {
        _session.LogStream.EntryAdded -= HandleEntryAdded;

        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _writer.Dispose();
        }
    }

    /// <summary>
    /// Writes one session log entry to disk and disables the writer after the first file I/O failure.
    /// </summary>
    private void HandleEntryAdded(LogEntry entry)
    {
        // Capture the failure and report it outside the writer lock because reporting appends another session log entry.
        Exception? writeFailure = null;
        lock (_syncRoot)
        {
            if (_isDisposed || _isDisabled)
            {
                return;
            }

            try
            {
                _writer.WriteLine(FormatEntry(entry));
            }
            catch (Exception ex) when (IsLogFileInfrastructureException(ex))
            {
                _isDisabled = true;
                writeFailure = ex;
            }
        }

        if (writeFailure != null)
        {
            ReportWriteFailure(writeFailure);
        }
    }

    /// <summary>
    /// Reports that the file sink stopped accepting writes while keeping subsequent session output in memory.
    /// </summary>
    private void ReportWriteFailure(Exception exception)
    {
        _session.Logger.LogError(exception, "Session log file '{SessionLogFilePath}' is no longer writable. Session output will remain available in memory only.", FilePath);
        TryLogApplicationInfrastructureFailure(exception, "Session log file '{SessionLogFilePath}' is no longer writable.", FilePath);
    }

    /// <summary>
    /// Formats a session log entry as one timestamped line while preserving any multiline exception details in the message.
    /// </summary>
    private static string FormatEntry(LogEntry entry)
    {
        // Include task identity only when the runtime attributed the line to a concrete execution task.
        string taskSegment = string.IsNullOrWhiteSpace(entry.TaskId)
            ? string.Empty
            : $" [{entry.TaskId}]";
        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{entry.Verbosity}]{taskSegment} {entry.Message}";
    }

    /// <summary>
    /// Identifies filesystem and path failures that should disable only the file sink, not the execution session itself.
    /// </summary>
    private static bool IsLogFileInfrastructureException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ObjectDisposedException;
    }

    /// <summary>
    /// Mirrors session-log infrastructure failures to the application logger when that process-wide sink is available.
    /// </summary>
    private static void TryLogApplicationInfrastructureFailure(Exception exception, string messageTemplate, params object[] args)
    {
        try
        {
            ApplicationLogger.Logger.LogError(exception, messageTemplate, args);
        }
        catch (InvalidOperationException)
        {
            // The session stream already received the failure; hosts without an app logger have no process-wide sink yet.
        }
    }
}
