using AirClean.Core.Models;
using AirClean.Core.Services;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "airclean-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_returns_defaults_when_the_file_is_missing()
    {
        var service = new SettingsService(_directory);

        var settings = service.Load();

        Assert.Equal("System", settings.Theme);
        Assert.Equal("en", settings.Language);
        Assert.Equal(1240, settings.WindowWidth);
        Assert.Equal(860, settings.WindowHeight);
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var service = new SettingsService(_directory);
        var settings = new AppSettings
        {
            Theme = "Dark",
            Language = "zh-CN",
            WindowWidth = 1500,
            WindowHeight = 900,
            WindowMaximized = true
        };

        Assert.True(service.Save(settings));

        var loaded = new SettingsService(_directory).Load();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("zh-CN", loaded.Language);
        Assert.Equal(1500, loaded.WindowWidth);
        Assert.Equal(900, loaded.WindowHeight);
        Assert.True(loaded.WindowMaximized);

        Assert.False(File.Exists(service.SettingsPath + ".tmp"));
    }

    [Fact]
    public void Broken_json_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ not json");

        var settings = new SettingsService(_directory).Load();

        Assert.Equal("System", settings.Theme);
        Assert.Equal(1240, settings.WindowWidth);
    }

    [Fact]
    public void Window_sizes_below_the_minimum_are_reset()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"windowWidth": 100, "windowHeight": 100}""");

        var settings = new SettingsService(_directory).Load();

        Assert.Equal(1240, settings.WindowWidth);
        Assert.Equal(860, settings.WindowHeight);
    }

    [Fact]
    public void Version_one_settings_get_v2_defaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """{"theme":"Dark","language":"zh-CN"}""");

        var settings = new SettingsService(_directory).Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal("zh-CN", settings.Language);
        Assert.Equal("Quick", settings.ScanMode);
        Assert.Empty(settings.Exclusions);
        Assert.Equal(100, settings.LargeFileThresholdMb);
        Assert.Equal(200, settings.LargeFileLimit);
        Assert.Equal(1, settings.DuplicateMinSizeMb);
        Assert.Equal(200, settings.DuplicateGroupLimit);
        Assert.Equal(8, settings.HashBudgetGb);
        Assert.True(settings.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void Invalid_v2_values_are_normalised_and_exclusions_are_cleaned()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            """
            {"scanMode":"Nope","largeFileThresholdMb":0,"largeFileLimit":999999,
             "duplicateMinSizeMb":0,"duplicateGroupLimit":5,"hashBudgetGb":0,
             "reminderIntervalDays":0,"exclusions":[" /a ","/a",""," /b "]}
            """);

        var settings = new SettingsService(_directory).Load();

        Assert.Equal("Quick", settings.ScanMode);
        Assert.Equal(100, settings.LargeFileThresholdMb);
        Assert.Equal(200, settings.LargeFileLimit);
        Assert.Equal(1, settings.DuplicateMinSizeMb);
        Assert.Equal(200, settings.DuplicateGroupLimit);
        Assert.Equal(8, settings.HashBudgetGb);
        Assert.Equal(7, settings.ReminderIntervalDays);
        Assert.Equal(["/a", "/b"], settings.Exclusions);
    }

    [Fact]
    public void V2_fields_survive_a_round_trip()
    {
        var service = new SettingsService(_directory);
        var settings = new AppSettings
        {
            ScanMode = "Full",
            Exclusions = [@"C:\Temp\keep"],
            LargeFileThresholdMb = 250,
            LargeFileLimit = 50,
            DuplicateMinSizeMb = 4,
            DuplicateGroupLimit = 25,
            HashBudgetGb = 16,
            CheckForUpdatesOnStartup = false,
            CloseToTray = true,
            MinimizeToTray = true,
            ReminderEnabled = true,
            ReminderIntervalDays = 14,
            RunAtLogin = true
        };

        Assert.True(service.Save(settings));

        var loaded = new SettingsService(_directory).Load();
        Assert.Equal("Full", loaded.ScanMode);
        Assert.Equal([@"C:\Temp\keep"], loaded.Exclusions);
        Assert.Equal(250, loaded.LargeFileThresholdMb);
        Assert.Equal(50, loaded.LargeFileLimit);
        Assert.Equal(4, loaded.DuplicateMinSizeMb);
        Assert.Equal(25, loaded.DuplicateGroupLimit);
        Assert.Equal(16, loaded.HashBudgetGb);
        Assert.False(loaded.CheckForUpdatesOnStartup);
        Assert.True(loaded.CloseToTray);
        Assert.True(loaded.MinimizeToTray);
        Assert.True(loaded.ReminderEnabled);
        Assert.Equal(14, loaded.ReminderIntervalDays);
        Assert.True(loaded.RunAtLogin);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // A leaked temp directory must not fail the test run.
        }
    }
}
