using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RuntimeExecutionTaskMetrics = LocalAutomation.Runtime.ExecutionTaskMetrics;

namespace LocalAutomation.Avalonia.Controls;

/// <summary>
/// Renders shared execution metric pills and raises filter clicks from the WARN and ERR badge affordances.
/// </summary>
public partial class ExecutionMetricsStrip : UserControl
{
    /// <summary>
    /// Identifies the raw execution metrics bound into the shared strip.
    /// </summary>
    public static readonly StyledProperty<RuntimeExecutionTaskMetrics> MetricsProperty =
        AvaloniaProperty.Register<ExecutionMetricsStrip, RuntimeExecutionTaskMetrics>(nameof(Metrics));

    /// <summary>
    /// Identifies the raw duration derived from the metrics.
    /// </summary>
    public static readonly StyledProperty<TimeSpan?> DurationProperty =
        AvaloniaProperty.Register<ExecutionMetricsStrip, TimeSpan?>(nameof(Duration));

    /// <summary>
    /// Identifies the warning count derived from the raw metrics.
    /// </summary>
    public static readonly StyledProperty<int> WarningCountProperty =
        AvaloniaProperty.Register<ExecutionMetricsStrip, int>(nameof(WarningCount));

    /// <summary>
    /// Identifies the error count derived from the raw metrics.
    /// </summary>
    public static readonly StyledProperty<int> ErrorCountProperty =
        AvaloniaProperty.Register<ExecutionMetricsStrip, int>(nameof(ErrorCount));

    /// <summary>
    /// Identifies whether the warning accent should be enabled for the current metrics.
    /// </summary>
    public static readonly StyledProperty<bool> HasWarningsProperty =
        AvaloniaProperty.Register<ExecutionMetricsStrip, bool>(nameof(HasWarnings));

    /// <summary>
    /// Identifies whether the error accent should be enabled for the current metrics.
    /// </summary>
    public static readonly StyledProperty<bool> HasErrorsProperty =
        AvaloniaProperty.Register<ExecutionMetricsStrip, bool>(nameof(HasErrors));

    /// <summary>
    /// Identifies the inherited WARN filter state shared by every metric strip in the selected workspace.
    /// </summary>
    public static readonly AttachedProperty<bool> IsWarningFilterActiveProperty =
        AvaloniaProperty.RegisterAttached<ExecutionMetricsStrip, Control, bool>(
            "IsWarningFilterActive",
            inherits: true);

    /// <summary>
    /// Identifies the inherited ERR filter state shared by every metric strip in the selected workspace.
    /// </summary>
    public static readonly AttachedProperty<bool> IsErrorFilterActiveProperty =
        AvaloniaProperty.RegisterAttached<ExecutionMetricsStrip, Control, bool>(
            "IsErrorFilterActive",
            inherits: true);

    /// <summary>
    /// Identifies the routed event raised when a WARN badge is clicked.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> WarningFilterClickedEvent =
        RoutedEvent.Register<ExecutionMetricsStrip, RoutedEventArgs>(
            nameof(WarningFilterClicked),
            RoutingStrategies.Bubble);

    /// <summary>
    /// Identifies the routed event raised when an ERR badge is clicked.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> ErrorFilterClickedEvent =
        RoutedEvent.Register<ExecutionMetricsStrip, RoutedEventArgs>(
            nameof(ErrorFilterClicked),
            RoutingStrategies.Bubble);

    /// <summary>
    /// Raised when the WARN badge is clicked anywhere this strip is used.
    /// </summary>
    public event EventHandler<RoutedEventArgs> WarningFilterClicked
    {
        add => AddHandler(WarningFilterClickedEvent, value);
        remove => RemoveHandler(WarningFilterClickedEvent, value);
    }

    /// <summary>
    /// Raised when the ERR badge is clicked anywhere this strip is used.
    /// </summary>
    public event EventHandler<RoutedEventArgs> ErrorFilterClicked
    {
        add => AddHandler(ErrorFilterClickedEvent, value);
        remove => RemoveHandler(ErrorFilterClickedEvent, value);
    }

    /// <summary>
    /// Creates the shared execution metrics strip.
    /// </summary>
    public ExecutionMetricsStrip()
    {
        InitializeComponent();
        ApplyMetrics(Metrics);
    }

    /// <summary>
    /// Gets or sets the raw execution metrics displayed by the strip.
    /// </summary>
    public RuntimeExecutionTaskMetrics Metrics
    {
        get => GetValue(MetricsProperty);
        set => SetValue(MetricsProperty, value);
    }

    /// <summary>
    /// Gets the raw duration shown by the TIME pill.
    /// </summary>
    public TimeSpan? Duration
    {
        get => GetValue(DurationProperty);
        private set => SetValue(DurationProperty, value);
    }

    /// <summary>
    /// Gets the warning count shown in the WARN pill.
    /// </summary>
    public int WarningCount
    {
        get => GetValue(WarningCountProperty);
        private set => SetValue(WarningCountProperty, value);
    }

    /// <summary>
    /// Gets the error count shown in the ERR pill.
    /// </summary>
    public int ErrorCount
    {
        get => GetValue(ErrorCountProperty);
        private set => SetValue(ErrorCountProperty, value);
    }

    /// <summary>
    /// Gets whether the warning pill should use the warning accent.
    /// </summary>
    public bool HasWarnings
    {
        get => GetValue(HasWarningsProperty);
        private set => SetValue(HasWarningsProperty, value);
    }

    /// <summary>
    /// Gets whether the error pill should use the error accent.
    /// </summary>
    public bool HasErrors
    {
        get => GetValue(HasErrorsProperty);
        private set => SetValue(HasErrorsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the WARN badge should render in the active filter state.
    /// </summary>
    public bool IsWarningFilterActive
    {
        get => GetIsWarningFilterActive(this);
        set => SetIsWarningFilterActive(this, value);
    }

    /// <summary>
    /// Gets or sets whether the ERR badge should render in the active filter state.
    /// </summary>
    public bool IsErrorFilterActive
    {
        get => GetIsErrorFilterActive(this);
        set => SetIsErrorFilterActive(this, value);
    }

    /// <summary>
    /// Reads the inherited WARN filter state from any control in the metric-strip subtree.
    /// </summary>
    public static bool GetIsWarningFilterActive(Control control)
    {
        if (control == null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        return control.GetValue(IsWarningFilterActiveProperty);
    }

    /// <summary>
    /// Writes the inherited WARN filter state to a control that owns metric-strip descendants.
    /// </summary>
    public static void SetIsWarningFilterActive(Control control, bool value)
    {
        if (control == null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        control.SetValue(IsWarningFilterActiveProperty, value);
    }

    /// <summary>
    /// Reads the inherited ERR filter state from any control in the metric-strip subtree.
    /// </summary>
    public static bool GetIsErrorFilterActive(Control control)
    {
        if (control == null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        return control.GetValue(IsErrorFilterActiveProperty);
    }

    /// <summary>
    /// Writes the inherited ERR filter state to a control that owns metric-strip descendants.
    /// </summary>
    public static void SetIsErrorFilterActive(Control control, bool value)
    {
        if (control == null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        control.SetValue(IsErrorFilterActiveProperty, value);
    }

    /// <summary>
    /// Projects raw metrics into the strip's internal display properties whenever the single public Metrics input changes.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MetricsProperty && change.NewValue is RuntimeExecutionTaskMetrics metrics)
        {
            ApplyMetrics(metrics);
        }
    }

    /// <summary>
    /// Loads the compiled Avalonia markup for the metrics strip.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Raises the WARN filter routed event while leaving the actual filter policy outside the visual control.
    /// </summary>
    private void WarningFilter_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(WarningFilterClickedEvent, this));
        e.Handled = true;
    }

    /// <summary>
    /// Raises the ERR filter routed event while leaving the actual filter policy outside the visual control.
    /// </summary>
    private void ErrorFilter_Click(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ErrorFilterClickedEvent, this));
        e.Handled = true;
    }

    /// <summary>
    /// Derives all display-facing pill values from one raw metrics object so callers cannot pass inconsistent booleans.
    /// </summary>
    private void ApplyMetrics(RuntimeExecutionTaskMetrics metrics)
    {
        Duration = metrics.Duration;
        WarningCount = metrics.WarningCount;
        ErrorCount = metrics.ErrorCount;
        HasWarnings = metrics.WarningCount > 0;
        HasErrors = metrics.ErrorCount > 0;
    }
}
