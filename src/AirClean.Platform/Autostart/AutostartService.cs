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
        const string key = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        if (!enabled)
        {
            // Query first: a missing value is a successful disable, and its error text is
            // localized. Only a delete that actually finds the value can fail.
            var query = RunReg($"query \"{key}\" /v AirClean");
            if (query.ExitCode != 0)
            {
                return true;
            }

            var delete = RunReg($"delete \"{key}\" /v AirClean /f");
            if (delete.ExitCode != 0)
            {
                error = FirstLine(delete.StandardError, delete.StandardOutput);
                return false;
            }

            return true;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            error = "The executable path is unavailable.";
            return false;
        }

        var add = RunReg($"add \"{key}\" /v AirClean /t REG_SZ /d \"\\\"{executable}\\\"\" /f");
        if (add.ExitCode != 0)
        {
            error = FirstLine(add.StandardError, add.StandardOutput);
            return false;
        }

        return true;
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunReg(string arguments)
    {
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
            return (-1, string.Empty, "Could not start reg.exe.");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static string FirstLine(string stderr, string stdout)
    {
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? "reg.exe failed." : line.Trim();
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
