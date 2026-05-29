using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LocalAutomation.Avalonia.ViewModels;

namespace LocalAutomation.Avalonia;

/// <summary>
/// Hosts the global application settings editor for the current launcher.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Creates the settings window around its composed view model.
    /// </summary>
    public SettingsWindow(SettingsWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        Closed += HandleClosed;
    }

    /// <summary>
    /// Provides the XAML loader and designer entry point without composing runtime dependencies.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the strongly typed view model backing the settings window.
    /// </summary>
    private SettingsWindowViewModel ViewModel => (SettingsWindowViewModel)DataContext!;

    /// <summary>
    /// Closes the settings window from the footer action row.
    /// </summary>
    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Flushes pending saves and detaches from the shared settings object before the window is released.
    /// </summary>
    private void HandleClosed(object? sender, System.EventArgs e)
    {
        Closed -= HandleClosed;
        ViewModel.FlushPendingSave();
        ViewModel.Dispose();
    }

    /// <summary>
    /// Loads the compiled Avalonia markup for the settings window.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
