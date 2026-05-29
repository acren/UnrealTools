using System;
using LocalAutomation.Application;
using LocalAutomation.Runtime;

namespace LocalAutomation.Avalonia.ViewModels;

/// <summary>
/// Wraps an operation target with the display-friendly fields needed by the initial Avalonia parity shell.
/// </summary>
public sealed class TargetListItemViewModel : ViewModelBase
{
    private readonly TargetDiscoveryService _targets;

    /// <summary>
    /// Creates a target list item for the provided operation target.
    /// </summary>
    public TargetListItemViewModel(TargetDiscoveryService targets, IOperationTarget target)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        if (!_targets.IsTarget(target))
        {
            // Keep host-facing validation errors aligned with the active launcher identity so shell-specific UIs do not leak
            // the generic LocalAutomation product name in user-visible diagnostics.
            throw new ArgumentException($"Target is not recognized by the registered {App.ShellIdentity.ApplicationName} target catalog.", nameof(target));
        }

        Target = target;
    }

    /// <summary>
    /// Gets the underlying runtime target instance.
    /// </summary>
    public IOperationTarget Target { get; }

    /// <summary>
    /// Gets the display name shown in the target list.
    /// </summary>
    public string DisplayName => _targets.GetDisplayName(Target);

    /// <summary>
    /// Gets the target type label shown in summaries and list rows.
    /// </summary>
    public string TypeName => _targets.GetTypeName(Target);

    /// <summary>
    /// Gets the filesystem path or location backing the target.
    /// </summary>
    public string TargetPath => _targets.GetTargetPath(Target);

}
