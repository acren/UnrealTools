namespace SystemUtilities.Processes;

/// <summary>
/// Captures process facts; a missing exit code means none was observed, never an inferred success.
/// </summary>
public readonly record struct ProcessResult(int? ExitCode, bool WasCancelled);
