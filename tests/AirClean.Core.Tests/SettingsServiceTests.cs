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
