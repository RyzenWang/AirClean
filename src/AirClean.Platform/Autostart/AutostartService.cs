using System.Diagnostics;
using System.Text;

namespace AirClean.Platform.Autostart;

/// <summary>
/// Registers the current executable to start at login. Windows uses the per-user Run key
/// through reg.exe, macOS a LaunchAgent, Linux the XDG autostart folder. Disabling removes
/// the same entry; failures come back as a reason the settings window can show.
/// </summary>
public static class AutostartService
{
    public static bool TrySet(bool enabled, out string? error)
    {
        error = null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return TrySetWindows(enabled, out error);
            }

            if (OperatingSystem.IsMacOS())
            {
                return TrySetMac(enabled, out error);
            }

            if (OperatingSystem.IsLinux())
            {
                return TrySetLinux(enabled, out error);
            }

            error = "Autostart is not supported on this platform.";
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TrySetWindows(bool enabled, out string? error)
    {
        error = null;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            error = "The executable path is unavailable.";
            return false;
        }

        var arguments = enabled
            ? $"add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v AirClean /t REG_SZ /d \"\\\"{executable}\\\"\" /f"
            : "delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v AirClean /f";

        var info = new ProcessStartInfo("reg.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(info);
        if (process is null)
        {
            error = "Could not start reg.exe.";
            return false;
        }

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // Deleting a missing value is fine; anything else is a real failure.
        if (process.ExitCode != 0 && !(!enabled && stderr.Contains("unable to find", StringComparison.OrdinalIgnoreCase)))
        {
            error = stderr.Trim();
            return false;
        }

        return true;
    }

    private static bool TrySetMac(bool enabled, out string? error)
    {
        error = null;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents");
        var path = Path.Combine(directory, "com.airclean.app.plist");

        if (!enabled)
        {
            return TryDelete(path, out error);
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            error = "The executable path is unavailable.";
            return false;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, BuildPlist(executable), new UTF8Encoding(false));
        return true;
    }

    private static bool TrySetLinux(bool enabled, out string? error)
    {
        error = null;
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDirectory = string.IsNullOrWhiteSpace(configHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : configHome;
        var directory = Path.Combine(baseDirectory, "autostart");
        var path = Path.Combine(directory, "airclean.desktop");

        if (!enabled)
        {
            return TryDelete(path, out error);
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            error = "The executable path is unavailable.";
            return false;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, BuildDesktopEntry(executable), new UTF8Encoding(false));
        return true;
    }

    private static bool TryDelete(string path, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal static string BuildDesktopEntry(string executable) =>
        "[Desktop Entry]\n" +
        "Type=Application\n" +
        "Name=AirClean\n" +
        "Comment=Light disk cleaner\n" +
        $"Exec=\"{executable.Replace("\"", "\\\"", StringComparison.Ordinal)}\"\n" +
        "Terminal=false\n" +
        "X-GNOME-Autostart-enabled=true\n";

    internal static string BuildPlist(string executable)
    {
        var escaped = System.Security.SecurityElement.Escape(executable) ?? executable;
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>com.airclean.app</string>
              <key>ProgramArguments</key>
              <array>
                <string>{escaped}</string>
              </array>
              <key>RunAtLoad</key>
              <true/>
            </dict>
            </plist>
            """;
    }
}
