using System.Text.Json;
using AirClean.Core.Models;

namespace AirClean.Core.Services;

/// <summary>
/// Small JSON settings store in the platform's per-user application data folder.
/// Writes atomically through a temporary file, and never lets a broken settings file
/// stop the application from starting.
/// </summary>
public sealed class SettingsService
{
    private const double MinimumWindowWidth = 920;
    private const double MinimumWindowHeight = 620;

    private readonly object _gate = new();

    public SettingsService(string? settingsDirectory = null)
    {
        SettingsDirectory = settingsDirectory ?? GetDefaultDirectory();
        SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    }

    public string SettingsDirectory { get; }

    public string SettingsPath { get; }

    public static string GetDefaultDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AirClean");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "AirClean");
        }

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(configHome))
        {
            return Path.Combine(configHome, "AirClean");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "AirClean");
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new AppSettings();
                }

                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
                return Normalize(settings ?? new AppSettings());
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    public bool Save(AppSettings settings)
    {
        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(SettingsDirectory);
                settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
                var temporaryPath = SettingsPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
                File.Move(temporaryPath, SettingsPath, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Repairs values that came from disk (or from a settings draft).</summary>
    public static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;

        if (double.IsNaN(settings.WindowWidth) || settings.WindowWidth < MinimumWindowWidth)
        {
            settings.WindowWidth = 1240;
        }

        if (double.IsNaN(settings.WindowHeight) || settings.WindowHeight < MinimumWindowHeight)
        {
            settings.WindowHeight = 860;
        }

        settings.Theme = settings.Theme switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => "System"
        };

        settings.Language = settings.Language switch
        {
            "zh-CN" => "zh-CN",
            _ => "en"
        };

        settings.ScanMode = settings.ScanMode switch
        {
            "Full" => "Full",
            _ => "Quick"
        };

        settings.Exclusions = (settings.Exclusions ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        settings.LargeFileThresholdMb = InRange(settings.LargeFileThresholdMb, 1, 10_240, 100);
        settings.LargeFileLimit = InRange(settings.LargeFileLimit, 10, 2_000, 200);
        settings.DuplicateMinSizeMb = InRange(settings.DuplicateMinSizeMb, 1, 1_024, 1);
        settings.DuplicateGroupLimit = InRange(settings.DuplicateGroupLimit, 10, 2_000, 200);
        settings.HashBudgetGb = InRange(settings.HashBudgetGb, 1, 64, 8);
        settings.ReminderIntervalDays = InRange(settings.ReminderIntervalDays, 1, 365, 7);

        return settings;
    }

    private static int InRange(int value, int minimum, int maximum, int fallback) =>
        value < minimum || value > maximum ? fallback : value;
}
