using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using LocalAutomation.Application;
using LocalAutomation.Avalonia.ViewModels;
using LocalAutomation.Core;
using LocalAutomation.Runtime;
using Serilog.Events;

namespace LocalAutomation.Avalonia.Controls;

/// <summary>
/// Owns execution-graph node context-menu behavior, including selection, clipboard actions, temp log export, and status
/// reporting for one workspace graph surface.
/// </summary>
internal sealed class ExecutionGraphNodeContextMenuController
{
    private const string ClipboardUnavailableMessage = "Clipboard access is not available in this window.";

    private readonly Func<IClipboard?> _getClipboard;
    private readonly Action<string> _setStatus;
    private readonly Func<ExecutionSessionLog?> _getCurrentSessionLog;

    /// <summary>
    /// Creates the context-menu controller around shell-owned clipboard, status, and current runtime-log lookup callbacks.
    /// </summary>
    public ExecutionGraphNodeContextMenuController(
        Func<IClipboard?> getClipboard,
        Action<string> setStatus,
        Func<ExecutionSessionLog?> getCurrentSessionLog)
    {
        _getClipboard = getClipboard ?? throw new ArgumentNullException(nameof(getClipboard));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _getCurrentSessionLog = getCurrentSessionLog ?? throw new ArgumentNullException(nameof(getCurrentSessionLog));
    }

    /// <summary>
    /// Binds the graph-node context menu to one retained node control and uses the supplied selector for node activation.
    /// </summary>
    public void Bind(ExecutionGraphNodeControlBase control, Action<ExecutionNodeViewModel> selectNode)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(selectNode);

        control.ContextRequested += (_, _) => SelectCurrentNode(control, selectNode);
        control.ContextMenu = CreateNodeContextMenu(control, selectNode);
    }

    /// <summary>
    /// Creates the fixed graph-node context menu used by both task cards and group containers.
    /// </summary>
    private ContextMenu CreateNodeContextMenu(ExecutionGraphNodeControlBase control, Action<ExecutionNodeViewModel> selectNode)
    {
        MenuItem copyNameItem = CreateNodeActionMenuItem("Copy Name", async (_, e) => await HandleCopyNameMenuItemClickAsync(control, selectNode, e));
        MenuItem copyPathItem = CreateNodeActionMenuItem("Copy Path", async (_, e) => await HandleCopyPathMenuItemClickAsync(control, selectNode, e));
        MenuItem copyLogPathItem = CreateNodeActionMenuItem("Copy Log File Path", async (_, e) => await HandleCopyLogPathMenuItemClickAsync(control, selectNode, e));

        ContextMenu menu = new()
        {
            Items =
            {
                copyNameItem,
                copyPathItem,
                copyLogPathItem
            }
        };

        menu.Opening += (_, _) =>
        {
            /* Name and path actions only need the bound node, while log export needs a live session log. Keeping that
               availability check here lets Avalonia render the unavailable log action as disabled instead of reporting
               an avoidable action-time failure. */
            bool hasNode = TryGetCurrentNode(control) != null;
            copyNameItem.IsEnabled = hasNode;
            copyPathItem.IsEnabled = hasNode;
            copyLogPathItem.IsEnabled = hasNode && _getCurrentSessionLog() != null;
        };

        return menu;
    }

    /// <summary>
    /// Creates one context-menu item and attaches the action handler that owns its behavior.
    /// </summary>
    private static MenuItem CreateNodeActionMenuItem(string header, EventHandler<RoutedEventArgs> clickHandler)
    {
        MenuItem menuItem = new() { Header = header };
        menuItem.Click += clickHandler;
        return menuItem;
    }

    /// <summary>
    /// Selects the bound node and copies its short task title to the current window clipboard.
    /// </summary>
    private async Task HandleCopyNameMenuItemClickAsync(ExecutionGraphNodeControlBase control, Action<ExecutionNodeViewModel> selectNode, RoutedEventArgs e)
    {
        e.Handled = true;
        ExecutionNodeViewModel node = SelectCurrentNode(control, selectNode);
        if (!await TryCopyTextAsync(node.Task.Title))
        {
            return;
        }

        _setStatus($"Copied task name '{node.Task.Title}'.");
    }

    /// <summary>
    /// Selects the bound node and copies its human-readable ancestor path to the current window clipboard.
    /// </summary>
    private async Task HandleCopyPathMenuItemClickAsync(ExecutionGraphNodeControlBase control, Action<ExecutionNodeViewModel> selectNode, RoutedEventArgs e)
    {
        e.Handled = true;
        ExecutionNodeViewModel node = SelectCurrentNode(control, selectNode);
        if (!await TryCopyTextAsync(node.Task.DisplayPath))
        {
            return;
        }

        _setStatus($"Copied task path for '{node.Task.Title}'.");
    }

    /// <summary>
    /// Selects the bound node, exports its scoped runtime log to a temp file, and copies that file path to the clipboard.
    /// </summary>
    private async Task HandleCopyLogPathMenuItemClickAsync(ExecutionGraphNodeControlBase control, Action<ExecutionNodeViewModel> selectNode, RoutedEventArgs e)
    {
        e.Handled = true;
        ExecutionNodeViewModel node = SelectCurrentNode(control, selectNode);
        ExecutionSessionLog? sessionLog = _getCurrentSessionLog();
        if (sessionLog == null)
        {
            _setStatus($"No task log is available for '{node.Task.Title}'.");
            return;
        }

        IClipboard? clipboard = GetClipboardOrReportUnavailable();
        if (clipboard == null)
        {
            return;
        }

        string exportPath = WriteTaskLogToTempFile(sessionLog, node);
        await clipboard.SetTextAsync(exportPath);
        _setStatus($"Copied task log path for '{node.Task.Title}'.");
    }

    /// <summary>
    /// Selects the node currently bound to a retained control and returns that same node for follow-up action logic.
    /// </summary>
    private static ExecutionNodeViewModel SelectCurrentNode(ExecutionGraphNodeControlBase control, Action<ExecutionNodeViewModel> selectNode)
    {
        ExecutionNodeViewModel node = GetRequiredCurrentNode(control);
        selectNode(node);
        return node;
    }

    /// <summary>
    /// Returns the node currently bound to a retained control, or null while the control is temporarily unbound.
    /// </summary>
    private static ExecutionNodeViewModel? TryGetCurrentNode(ExecutionGraphNodeControlBase control)
    {
        return control.DataContext as ExecutionNodeViewModel;
    }

    /// <summary>
    /// Returns the node currently bound to a retained control and fails visibly when interaction reaches an unbound node.
    /// </summary>
    private static ExecutionNodeViewModel GetRequiredCurrentNode(ExecutionGraphNodeControlBase control)
    {
        return TryGetCurrentNode(control)
            ?? throw new InvalidOperationException($"{control.GetType().Name} requires an {nameof(ExecutionNodeViewModel)} data context before graph-node actions can run.");
    }

    /// <summary>
    /// Resolves the current window clipboard and reports through the workspace status path when it is unavailable.
    /// </summary>
    private IClipboard? GetClipboardOrReportUnavailable()
    {
        IClipboard? clipboard = _getClipboard();
        if (clipboard == null)
        {
            _setStatus(ClipboardUnavailableMessage);
        }

        return clipboard;
    }

    /// <summary>
    /// Copies plain text to the current window clipboard when clipboard access is available.
    /// </summary>
    private async Task<bool> TryCopyTextAsync(string text)
    {
        IClipboard? clipboard = GetClipboardOrReportUnavailable();
        if (clipboard == null)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        return true;
    }

    /// <summary>
    /// Writes the requested task subtree log to one readable temp file and returns its absolute path.
    /// </summary>
    private static string WriteTaskLogToTempFile(ExecutionSessionLog sessionLog, ExecutionNodeViewModel node)
    {
        string fileName = $"localautomation-task-log-{DateTimeOffset.Now:yyyyMMdd_HHmmssfff}-{ExecutionPathConventions.MakeCompactSegment(node.Task.DisplayPath, 48)}.log";
        string filePath = Path.Combine(Path.GetTempPath(), fileName);
        using StreamWriter writer = new(filePath);
        IReadOnlyList<LogEvent> scopedEvents = sessionLog.GetTaskScopedEvents(node.Id);
        foreach (LogEvent scopedEvent in scopedEvents)
        {
            UnifiedLogTextFormatting.Format(writer, scopedEvent);
        }

        return filePath;
    }
}
