using AirClean.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;

namespace AirClean.App.Services;

/// <summary>
/// Owns the application theme. Avalonia's requested theme variant is process-wide, so a
/// single instance applies it instead of every window fighting over the setting.
/// </summary>
public sealed class ThemeService
{
    public const string SystemMode = "System";
    public const string LightMode = "Light";
    public const string DarkMode = "Dark";

    private readonly AppSettings _settings;
    private readonly Action _persist;

    public ThemeService(AppSettings settings, Action persist)
    {
        _settings = settings;
        _persist = persist;

        Mode = Normalize(settings.Theme);
        _settings.Theme = Mode;
        IsDark = ResolveIsDark(Mode);
        Apply();

        if (Application.Current?.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
        }
    }

    /// <summary>One of <c>System</c>, <c>Light</c>, <c>Dark</c>.</summary>
    public string Mode { get; private set; }

    /// <summary>Resolved appearance; <c>System</c> follows the operating system.</summary>
    public bool IsDark { get; private set; }

    public bool IsSystem => string.Equals(Mode, SystemMode, StringComparison.Ordinal);

    public event EventHandler? Changed;

    public void Set(string mode, bool persist)
    {
        Mode = Normalize(mode);
        _settings.Theme = Mode;
        IsDark = ResolveIsDark(Mode);

        // IsDark may be unchanged (system-light to light), so apply explicitly.
        Apply();

        if (persist)
        {
            _persist();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cycles System → Light → Dark → System.</summary>
    public void Cycle() => Set(
        Mode switch
        {
            SystemMode => LightMode,
            LightMode => DarkMode,
            _ => SystemMode
        },
        persist: true);

    private static string Normalize(string? mode) => mode switch
    {
        LightMode => LightMode,
        DarkMode => DarkMode,
        _ => SystemMode
    };

    private static bool ResolveIsDark(string mode) => mode switch
    {
        DarkMode => true,
        LightMode => false,
        _ => SystemIsDark()
    };

    private static bool SystemIsDark()
    {
        try
        {
            return Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant
                == PlatformThemeVariant.Dark;
        }
        catch
        {
            // Headless hosts may not expose theme values.
            return false;
        }
    }

    private void Apply()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = Mode switch
        {
            LightMode => ThemeVariant.Light,
            DarkMode => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (!IsSystem)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsSystem)
            {
                return;
            }

            IsDark = e.ThemeVariant == PlatformThemeVariant.Dark;
            Apply();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
