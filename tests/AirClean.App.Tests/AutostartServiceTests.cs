using AirClean.Platform.Autostart;
using Xunit;

namespace AirClean.App.Tests;

public sealed class AutostartServiceTests
{
    [Fact]
    public void The_desktop_entry_quotes_the_executable()
    {
        var entry = AutostartService.BuildDesktopEntry("/tmp/Air Clean/AirClean");

        Assert.Contains("Type=Application", entry);
        Assert.Contains("Exec=\"/tmp/Air Clean/AirClean\"", entry);
        Assert.Contains("X-GNOME-Autostart-enabled=true", entry);
    }

    [Fact]
    public void The_launch_agent_escapes_xml()
    {
        var plist = AutostartService.BuildPlist("/tmp/a&b/AirClean");

        Assert.Contains("com.airclean.app", plist);
        Assert.Contains("/tmp/a&amp;b/AirClean", plist);
        Assert.Contains("<key>RunAtLoad</key>", plist);
    }
}
