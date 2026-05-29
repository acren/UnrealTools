using Avalonia.Controls;
using LocalAutomation.Avalonia.ViewModels;

namespace LocalAutomation.Avalonia;

/// <summary>
/// Hosts the composed Avalonia shell and wires the shared view model to the extracted panel views.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes the shell window and assigns the composed main-window view model.
    /// </summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        Closed += HandleClosed;
    }

    /// <summary>
    /// Provides the XAML loader and designer entry point without composing runtime dependencies.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the strongly typed view model for the shell window.
    /// </summary>
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    /// <summary>
    /// Flushes any pending debounced session save before the window fully closes.
    /// </summary>
    private void HandleClosed(object? sender, System.EventArgs e)
    {
        Closed -= HandleClosed;
        ViewModel.FlushPendingSessionState();
        ViewModel.Dispose();
    }

}
