using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LocalAutomation.Application;
using LocalAutomation.Avalonia.Bootstrap;
using LocalAutomation.Avalonia.Controls;
using LocalAutomation.Avalonia.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using AvaloniaApplication = Avalonia.Application;

namespace LocalAutomation.Avalonia;

/// <summary>
/// Wires the shared Avalonia shell to the launcher-provided identity and application services.
/// </summary>
public partial class App : AvaloniaApplication
{
    /// <summary>
    /// Gets the launcher-provided shell identity values that control product naming and host-owned storage locations.
    /// </summary>
    public static ShellIdentity ShellIdentity { get; private set; } = ShellIdentity.LocalAutomation;

    /// <summary>
    /// Holds the launcher-composed service provider that constructs shell windows and application services.
    /// </summary>
    private static ServiceProvider? Services { get; set; }

    /// <summary>
    /// Replaces the current launcher-provided service provider.
    /// </summary>
    public static void ConfigureServices(ServiceProvider services)
    {
        Services = services ?? throw new System.ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Creates a settings window from the launcher-composed provider.
    /// </summary>
    public static SettingsWindow CreateSettingsWindow()
    {
        return ResolveRequiredService<SettingsWindow>();
    }

    /// <summary>
    /// Resolves a required shell service from the launcher-composed provider inside the composition boundary.
    /// </summary>
    private static T ResolveRequiredService<T>() where T : notnull
    {
        if (Services == null)
        {
            throw new System.InvalidOperationException("The Avalonia shell service provider has not been configured.");
        }

        return Services.GetRequiredService<T>();
    }

    /// <summary>
    /// Replaces the current launcher-provided shell identity before the shell initializes any windows or host-owned files.
    /// </summary>
    public static void ConfigureShellIdentity(ShellIdentity shellIdentity)
    {
        ShellIdentity = shellIdentity ?? throw new System.ArgumentNullException(nameof(shellIdentity));
    }

    /// <summary>
    /// Loads the application XAML resources and theme definitions.
    /// </summary>
    public override void Initialize()
    {
        PropertyGridTypeDescriptorRegistrar.Register();
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Creates the initial desktop window for the placeholder shell.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ApplicationSettings settings = ResolveRequiredService<ApplicationSettingsService>().Settings;
            PerformanceTelemetryListener.Start(
                settings.EnablePerformanceTelemetry,
                System.TimeSpan.FromMilliseconds(settings.MinimumPerformanceTelemetryMilliseconds),
                System.TimeSpan.FromMilliseconds(settings.MinimumVisiblePerformanceTelemetryScopeMilliseconds));
            MainWindow mainWindow = ResolveRequiredService<MainWindow>();
            mainWindow.Title = ShellIdentity.WindowTitle;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
