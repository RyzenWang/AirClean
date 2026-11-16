using System.ComponentModel;
using AirClean.App.Services;
using AirClean.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PathShape = Avalonia.Controls.Shapes.Path;

namespace AirClean.App.Views;

public partial class MainWindow : Window
{
    private const double RingRadius = 47;
    private const double SpinnerRadius = 7.8;

    private MainWindowViewModel? _viewModel;
    private ItemsControl? _categoryList;
    private bool _ready;
    private DispatcherTimer? _spinnerTimer;
    private PathShape? _spinnerArc;
    private double _spinnerAngle;

    public MainWindow()
    {
        InitializeComponent();
        _spinnerArc = this.FindControl<PathShape>("CleaningSpinnerArc");
        if (_spinnerArc is not null)
        {
            _spinnerArc.Data = BuildSpinnerArc(-90);
        }

        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _spinnerTimer.Tick += OnSpinnerTick;

        // Arrows move between the category rows; Tab alone would make a long list tedious.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Up and Down walk the focusable controls inside the category list. Only when the focus
    /// is already in that list, so the combo box, text inputs and page scrolling keep their
    /// own arrow behaviour.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        _categoryList ??= this.FindControl<ItemsControl>("CategoryList");
        if (_categoryList is not { IsEffectivelyVisible: true })
        {
            return;
        }

        if (FocusManager?.GetFocusedElement() is not Control focused ||
            !_categoryList.GetVisualDescendants().Contains(focused))
        {
            return;
        }

        var stops = _categoryList.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Focusable && control.IsEffectivelyVisible)
            .ToList();

        var index = stops.IndexOf(focused);
        if (index < 0)
        {
            return;
        }

        var next = Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, stops.Count - 1);
        if (next == index)
        {
            return;
        }

        stops[next].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ready = true;
        _spinnerTimer?.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _spinnerTimer?.Stop();
        base.OnClosed(e);
    }

    private void OnSpinnerTick(object? sender, EventArgs e)
    {
        if (_viewModel?.IsDeleting != true)
        {
            return;
        }

        _spinnerArc ??= this.FindControl<PathShape>("CleaningSpinnerArc");
        if (_spinnerArc is null)
        {
            return;
        }

        _spinnerAngle = (_spinnerAngle + 12) % 360;
        _spinnerArc.Data = BuildSpinnerArc(-90 + _spinnerAngle);
    }

    /// <summary>
    /// Builds the spinner arc inside a fixed 18×18 box. Rotating the geometry itself
    /// keeps the visual centre at (9,9); rotating the old Arc control moved its origin.
    /// </summary>
    private static Geometry BuildSpinnerArc(double startAngle)
    {
        const double center = 9;
        const double sweep = 100;
        var start = PointOnCircle(center, SpinnerRadius, startAngle);
        var end = PointOnCircle(center, SpinnerRadius, startAngle + sweep);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false);
            context.ArcTo(end, new Size(SpinnerRadius, SpinnerRadius), 0, sweep > 180, SweepDirection.Clockwise);
            context.EndFigure(false);
        }

        return geometry;
    }

    private void OnVolumeChanged(object? sender, SelectionChangedEventArgs e)
    {
        // The initial binding also raises this event, and the welcome page owns its own
        // chips; only a real pick while results are on screen should start a scan.
        if (!_ready || DataContext is not MainWindowViewModel viewModel || !viewModel.IsResultsVisible)
        {
            return;
        }

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is VolumeItemViewModel volume)
        {
            viewModel.SelectVolumeCommand.Execute(volume);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.SettingsRequested -= OnSettingsRequested;
        }

        base.OnDataContextChanged(e);

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.SettingsRequested += OnSettingsRequested;
            _viewModel.AttachSuggestionClipboard(WriteSuggestionToClipboardAsync);
            RingArc.Data = BuildRingArc(_viewModel.DiskUsedRatio);
        }
    }

    /// <summary>
    /// The suggestion rows own the copy command but not the clipboard: the window hands
    /// the view model a plain delegate so the row stays testable off-screen.
    /// </summary>
    private async Task WriteSuggestionToClipboardAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        var window = new SettingsWindow(
            viewModel.Settings,
            viewModel.Theme,
            viewModel.Localization,
            viewModel.Persist,
            viewModel.IsAdminHelperAvailable);

        await window.ShowDialog(this);
        viewModel.RefreshSettings();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.DiskUsedRatio) && _viewModel is not null)
        {
            RingArc.Data = BuildRingArc(_viewModel.DiskUsedRatio);
        }
    }

    /// <summary>
    /// The ring is a real arc geometry in a 94×94 box, so its length is exactly the used
    /// share of the volume (an Ellipse dash pattern is measured in stroke widths, which
    /// makes the visible share hard to reason about).
    /// </summary>
    private static Geometry BuildRingArc(double usedRatio)
    {
        const double center = 47;
        var sweep = Math.Clamp(usedRatio, 0.001, 0.999) * 360.0;
        var start = PointOnCircle(center, RingRadius, -90);
        var end = PointOnCircle(center, RingRadius, -90 + sweep);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false);
            context.ArcTo(end, new Size(RingRadius, RingRadius), 0, sweep > 180, SweepDirection.Clockwise);
            context.EndFigure(false);
        }

        return geometry;
    }

    private static Point PointOnCircle(double center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnThemeSystem(object? sender, RoutedEventArgs e) => ViewModel?.SetThemeCommand.Execute(ThemeService.SystemMode);

    private void OnThemeLight(object? sender, RoutedEventArgs e) => ViewModel?.SetThemeCommand.Execute(ThemeService.LightMode);

    private void OnThemeDark(object? sender, RoutedEventArgs e) => ViewModel?.SetThemeCommand.Execute(ThemeService.DarkMode);

    private void OnLanguageEnglish(object? sender, RoutedEventArgs e) => ViewModel?.SetLanguageCommand.Execute(LocalizationService.English);

    private void OnLanguageChinese(object? sender, RoutedEventArgs e) => ViewModel?.SetLanguageCommand.Execute(LocalizationService.ChineseSimplified);
}
