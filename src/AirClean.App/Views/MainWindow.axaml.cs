using System.ComponentModel;
using AirClean.App.Services;
using AirClean.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AirClean.App.Views;

public partial class MainWindow : Window
{
    private const double RingRadius = 47;

    private MainWindowViewModel? _viewModel;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ready = true;
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
        }

        base.OnDataContextChanged(e);

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RingArc.Data = BuildRingArc(_viewModel.DiskUsedRatio);
        }
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
