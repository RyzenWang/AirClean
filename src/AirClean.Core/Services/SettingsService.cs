using System.Text.Json;
using AirClean.Core.Models;

namespace AirClean.Core.Services;

/// <summary>
/// Small JSON settings store in the platform's per-user application data folder.
/// Writes atomically through a temporary file and keeps the previous good copy as
/// <c>settings.json.bak</c>. A broken file never stops the application from starting, but
/// <see cref="LoadResult"/> reports it so callers can stop destructive work instead of
/// silently dropping the excluded folders.
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

    /// <summary>Last good copy, written before <see cref="SettingsPath"/> is replaced.</summary>
    public string BackupPath => SettingsPath + ".bak";

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

    public AppSettings Load() => LoadResult().Settings;

    /// <summary>
    /// Reads the settings file and says whether the read actually worked. A malformed or
    /// unreadable file falls back to <see cref="BackupPath"/> first; only when that is
    /// unusable too does the status become <see cref="SettingsLoadStatus.Corrupt"/> or
    /// <see cref="SettingsLoadStatus.Unreadable"/> with default settings.
    /// </summary>
    public SettingsLoadResult LoadResult()
    {
        lock (_gate)
        {
            if (!File.Exists(SettingsPath))
            {
                return new SettingsLoadResult(SettingsLoadStatus.FirstRun, new AppSettings());
            }

            try
            {
                return new SettingsLoadResult(SettingsLoadStatus.Ok, ReadSettings(SettingsPath));
            }
            catch (Exception error)
            {
                var status = error is JsonException or NotSupportedException or ArgumentException
                    ? SettingsLoadStatus.Corrupt
                    : SettingsLoadStatus.Unreadable;

                if (TryReadBackup(out var backup))
                {
                    return new SettingsLoadResult(
                        SettingsLoadStatus.RecoveredFromBackup,
                        backup,
                        error.Message);
                }

                return new SettingsLoadResult(status, new AppSettings(), error.Message);
            }
        }
    }

    private static AppSettings ReadSettings(string path)
    {
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
        return Normalize(settings ?? new AppSettings());
    }

    private bool TryReadBackup(out AppSettings settings)
    {
        settings = new AppSettings();

        try
        {
            if (!File.Exists(BackupPath))
            {
                return false;
            }

            settings = ReadSettings(BackupPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps the file that is about to be replaced. A file that does not parse is not
    /// promoted into the backup slot, so repairing a broken file cannot destroy the last
    /// good copy the user still has.
    /// </summary>
    private void KeepBackup()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            _ = ReadSettings(SettingsPath);
            File.Copy(SettingsPath, BackupPath, overwrite: true);
        }
        catch
        {
            // A missing backup is not a reason to fail the save.
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
                KeepBackup();
                var temporaryPath = SettingsPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
                File.Move(temporaryPath, SettingsPath, overwrite: true); // SAFE: replaces the settings file with the temp file this save just wrote
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
