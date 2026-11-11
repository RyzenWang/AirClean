using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;

namespace AirClean.App.Services;

/// <summary>
/// Optional system tray icon. Creation is best effort: Linux desktops without
/// StatusNotifier, or a headless session, simply return no service and the app keeps
/// working without a tray.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _open;
    private readonly NativeMenuItem _scan;
    private readonly NativeMenuItem _exit;

    private TrayIconService(
        TrayIcon tray,
        NativeMenuItem open,
        NativeMenuItem scan,
        NativeMenuItem exit)
    {
        _tray = tray;
        _open = open;
        _scan = scan;
        _exit = exit;
    }

    public static TrayIconService? TryCreate(
        LocalizationService localization,
        Action open,
        Action scan,
        Action exit)
    {
        try
        {
            var openItem = new NativeMenuItem(localization.T("L.TrayOpen"))
            {
                Command = new RelayCommand(open)
            };
            var scanItem = new NativeMenuItem(localization.T("L.TrayScan"))
            {
                Command = new RelayCommand(scan)
            };
            var exitItem = new NativeMenuItem(localization.T("L.TrayExit"))
            {
                Command = new RelayCommand(exit)
            };

            var menu = new NativeMenu();
            menu.Items.Add(openItem);
            menu.Items.Add(scanItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);

            using var stream = AssetLoader.Open(new Uri("avares://AirClean/Assets/airclean.png"));
            var tray = new TrayIcon
            {
                Icon = new WindowIcon(stream),
                ToolTipText = "AirClean",
                Menu = menu,
                IsVisible = true
            };

            tray.Clicked += (_, _) => open();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });

            return new TrayIconService(tray, openItem, scanItem, exitItem);
        }
        catch
        {
            return null;
        }
    }

    public void RefreshLanguage(LocalizationService localization)
    {
        _open.Header = localization.T("L.TrayOpen");
        _scan.Header = localization.T("L.TrayScan");
        _exit.Header = localization.T("L.TrayExit");
        _tray.ToolTipText = "AirClean";
    }

    public void ShowReminder(string message) =>
        _tray.ToolTipText = "AirClean · " + message;

    public void Dispose()
    {
        _tray.IsVisible = false;
        _tray.Dispose();
    }
}
