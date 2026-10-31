using System.Diagnostics;
using AirClean.Core.Platform;

namespace AirClean.Platform.Shell;

/// <summary>
/// "Show in folder" for the current OS. Explorer can select the file itself; Finder does
/// it with <c>open -R</c>; most Linux file managers only accept a directory, so we open
/// the parent folder there.
/// </summary>
internal sealed class ShellRevealService : IRevealService
{
    public bool IsSupported => true;

    public void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
                return;
            }

            var directory = Path.GetDirectoryName(path);

            if (OperatingSystem.IsMacOS())
            {
                var start = new ProcessStartInfo("open") { UseShellExecute = false };
                start.ArgumentList.Add("-R");
                start.ArgumentList.Add(path);
                Process.Start(start);
                return;
            }

            if (!string.IsNullOrEmpty(directory))
            {
                Process.Start(new ProcessStartInfo("xdg-open", directory) { UseShellExecute = true });
            }
        }
        catch
        {
            // Revealing is a convenience; never let it break the UI.
        }
    }
}
