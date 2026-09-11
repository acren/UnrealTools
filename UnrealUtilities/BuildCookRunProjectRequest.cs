namespace UnrealUtilities;

/// <summary>
/// Describes one complete project BuildCookRun invocation in terms of enabled phases and the explicit command
/// settings that should shape the generated UAT arguments.
/// </summary>
public readonly struct BuildCookRunProjectRequest
{
    /// <summary>Captures the phase and path choices used to construct one UAT invocation.</summary>
    public BuildCookRunProjectRequest(
        BuildCookRunProjectPhases phases,
        BuildConfiguration configuration = BuildConfiguration.Development,
        bool noDebugInfo = false,
        string? archiveDirectory = null,
        string? unrealExePath = null,
        string? additionalCookerOptions = null,
        string? stagingDirectory = null,
        string? cookOutputDirectory = null)
    {
        Phases = phases;
        Configuration = configuration;
        NoDebugInfo = noDebugInfo;
        ArchiveDirectory = archiveDirectory;
        UnrealExePath = unrealExePath;
        AdditionalCookerOptions = additionalCookerOptions;
        StagingDirectory = stagingDirectory;
        CookOutputDirectory = cookOutputDirectory;
    }

    /// <summary>The explicit BuildCookRun phases to turn into UAT flags for this invocation.</summary>
    public BuildCookRunProjectPhases Phases { get; }

    /// <summary>The client and server configuration BuildCookRun should use for this invocation.</summary>
    public BuildConfiguration Configuration { get; }

    /// <summary>Controls whether BuildCookRun should omit debug symbols from the packaged output.</summary>
    public bool NoDebugInfo { get; }

    /// <summary>When non-empty, instructs BuildCookRun to emit its archive layout to this directory.</summary>
    public string? ArchiveDirectory { get; }

    /// <summary>Overrides the cooker executable path when one specific cooker configuration should drive the cook.</summary>
    public string? UnrealExePath { get; }

    /// <summary>Supplies extra raw cooker arguments for flows that need one targeted cooker behavior toggle.</summary>
    public string? AdditionalCookerOptions { get; }

    /// <summary>Overrides the root directory that BuildCookRun uses for staged package output.</summary>
    public string? StagingDirectory { get; }

    /// <summary>Overrides the platform-specific directory that the cook commandlet writes cooked payloads into.</summary>
    public string? CookOutputDirectory { get; }

    /// <summary>Returns whether one specific BuildCookRun phase is enabled for the request.</summary>
    public bool HasPhase(BuildCookRunProjectPhases phase)
    {
        return (Phases & phase) == phase;
    }
}
