using System.Diagnostics;
using System.Text;
using AirClean.Core.Trash;

namespace AirClean.Platform.Unix;

/// <summary>
/// Trash can for macOS (<c>~/.Trash</c>) and Linux (freedesktop.org Trash specification).
/// Items are moved, not copied, so the operation stays fast on the same volume.
/// </summary>
internal sealed class UnixTrashService : ITrashService
{
    private readonly bool _isMac = OperatingSystem.IsMacOS();

    private string Root { get; }

    public UnixTrashService()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (_isMac)
        {
            Root = Path.Combine(home, ".Trash");
        }
        else
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var baseDirectory = string.IsNullOrWhiteSpace(dataHome)
                ? Path.Combine(home, ".local", "share")
                : dataHome;

            Root = Path.Combine(baseDirectory, "Trash");
        }
    }

    public bool IsSupported => true;

    private string FilesDirectory => _isMac ? Root : Path.Combine(Root, "files");

    private string InfoDirectory => Path.Combine(Root, "info");

    public TrashSample? Probe(string? rootPath = null)
    {
        try
        {
            if (!Directory.Exists(FilesDirectory))
            {
                return new TrashSample(0, 0);
            }

            long bytes = 0;
            var count = 0;

            foreach (var file in Directory.EnumerateFiles(FilesDirectory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                    count++;
                }
                catch
                {
                    // A file that vanished mid-walk simply does not count.
                }
            }

            return new TrashSample(bytes, count);
        }
        catch
        {
            return null;
        }
    }

    public bool MoveToTrash(string path, out string? error)
    {
        error = null;

        try
        {
            Directory.CreateDirectory(FilesDirectory);

            var name = Path.GetFileName(path);
            var destination = UniqueDestination(name);
            var source = path;

            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else
            {
                File.Move(source, destination);
            }

            if (!_isMac)
            {
                WriteTrashInfo(name, source);
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public long Empty(string? rootPath = null)
    {
        var before = Probe()?.Bytes ?? 0;

        try
        {
            if (Directory.Exists(FilesDirectory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(FilesDirectory))
                {
                    try
                    {
                        if (Directory.Exists(entry))
                        {
                            Directory.Delete(entry, recursive: true);
                        }
                        else
                        {
                            File.Delete(entry);
                        }
                    }
                    catch
                    {
                        // Locked entries stay; the caller still gets an accurate measurement.
                    }
                }
            }

            if (Directory.Exists(InfoDirectory))
            {
                foreach (var info in Directory.EnumerateFiles(InfoDirectory, "*.trashinfo"))
                {
                    try
                    {
                        File.Delete(info);
                    }
                    catch
                    {
                        // Ignored.
                    }
                }
            }
        }
        catch
        {
            // Measured below.
        }

        var after = Probe()?.Bytes ?? 0;
        return Math.Max(0, before - after);
    }

    public void OpenTrash()
    {
        try
        {
            var command = _isMac ? "open" : "xdg-open";
            Process.Start(new ProcessStartInfo(command, Root) { UseShellExecute = true });
        }
        catch
        {
            // Opening the trash is a convenience only.
        }
    }

    private string UniqueDestination(string name)
    {
        var destination = Path.Combine(FilesDirectory, name);
        var index = 2;

        while (File.Exists(destination) || Directory.Exists(destination))
        {
            destination = Path.Combine(FilesDirectory, $"{name} ({index})");
            index++;
        }

        return destination;
    }

    private void WriteTrashInfo(string name, string originalPath)
    {
        try
        {
            Directory.CreateDirectory(InfoDirectory);

            var infoPath = Path.Combine(InfoDirectory, name + ".trashinfo");
            var content = new StringBuilder()
                .AppendLine("[Trash Info]")
                .Append("Path=").AppendLine(Uri.EscapeDataString(originalPath))
                .Append("DeletionDate=").AppendLine(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"))
                .ToString();

            File.WriteAllText(infoPath, content);
        }
        catch
        {
            // The file is already in the trash; missing metadata only affects "restore".
        }
    }
}
