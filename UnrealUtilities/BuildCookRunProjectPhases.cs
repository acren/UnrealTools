using System;

namespace UnrealUtilities;

/// <summary>Identifies the BuildCookRun phases enabled for one project UAT invocation.</summary>
[Flags]
public enum BuildCookRunProjectPhases
{
    None = 0,
    Build = 1 << 0,
    Cook = 1 << 1,
    Stage = 1 << 2,
    Pak = 1 << 3,
    Package = 1 << 4,
}
