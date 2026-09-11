using System;
using System.Collections.Generic;

namespace UnrealUtilities;

/// <summary>Describes explicit launch flags and automation output independently of an application's option state.</summary>
public sealed record UnrealLaunchRequest
{
    public string? ProjectDescriptorPath { get; init; }
    public IReadOnlyList<TraceChannel> TraceChannels { get; init; } = Array.Empty<TraceChannel>();
    public bool StompMalloc { get; init; }
    public bool WaitForAttach { get; init; }
    public bool NoMessaging { get; init; }
    public bool DdcForceMemoryCache { get; init; }
    public bool Multiprocess { get; init; }

    /// <summary>Null disables automation; an empty filter explicitly requests Unreal's empty-filter behavior.</summary>
    public string? AutomationFilter { get; init; }
    public bool Headless { get; init; }

    /// <summary>Names the final export directory required when automation is enabled.</summary>
    public string? ReportOutputDirectory { get; init; }
}
