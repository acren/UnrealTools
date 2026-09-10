using LocalAutomation.Runtime;
using EngineModel = UnrealAutomationCommon.Unreal.Engine;

namespace LocalAutomation.Extensions.Unreal.Targets;

/// <summary>Provides a runtime package target for a selected engine's staged-build layout.</summary>
public interface IPackageProvider : IOperationTarget
{
    /// <summary>Returns the available package target, or null when no package has been staged.</summary>
    Package? GetProvidedPackage(EngineModel engineContext);
}
