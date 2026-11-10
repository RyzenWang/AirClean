using System.Diagnostics;
using System.Globalization;
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

    public TrashMoveResult MoveToTrash(string path)
    {
        if (OperatingSystem.IsMacOS() && MacTrash.TryMove(path, out var macPath, out _))
        {
            return TrashMoveResult.Moved(macPath);
        }

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

            return TrashMoveResult.Moved(destination);
        }
        catch (Exception exception)
        {
            return TrashMoveResult.Failed(exception.Message);
        }
    }

    public bool TryRestore(string? trashPath, string originalPath, out string? error)
    {
        error = null;

        try
        {
            var source = ResolveTrashPath(trashPath, originalPath);
            if (source is null)
            {
                error = "The item is no longer in the trash.";
                return false;
            }

            if (File.Exists(originalPath) || Directory.Exists(originalPath))
            {
                error = "The original location already contains an item with that name.";
                return false;
            }

            var parent = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                error = "The original folder no longer exists.";
                return false;
            }

            if (Directory.Exists(source))
            {
                Directory.Move(source, originalPath);
            }
            else
            {
                File.Move(source, originalPath);
            }

            if (!_isMac)
            {
                RemoveTrashInfo(source, originalPath);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Uses the path the caller recorded when it exists; otherwise walks the .trashinfo
    /// files and picks the newest entry whose original path matches. The fallback is what
    /// makes restore work after a restart on a journal written before the path was known.
    /// </summary>
    private string? ResolveTrashPath(string? trashPath, string originalPath)
    {
        if (!string.IsNullOrWhiteSpace(trashPath) && (File.Exists(trashPath) || Directory.Exists(trashPath)))
        {
            return trashPath;
        }

        if (_isMac)
        {
            // macOS restore currently relies on the path recorded when the item was moved.
            // The NSFileManager upgrade (AC-305) is what adds external-volume support.
            return null;
        }

        if (!Directory.Exists(InfoDirectory) || !Directory.Exists(FilesDirectory))
        {
            return null;
        }

        string? best = null;
        var bestDeletedAt = DateTimeOffset.MinValue;

        foreach (var infoFile in Directory.EnumerateFiles(InfoDirectory, "*.trashinfo"))
        {
            var info = ReadTrashInfo(infoFile);
            if (info is null || !string.Equals(info.Value.Path, originalPath, StringComparison.Ordinal))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(infoFile);
            var candidate = Path.Combine(FilesDirectory, name);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                continue;
            }

            if (info.Value.DeletedAt > bestDeletedAt)
            {
                bestDeletedAt = info.Value.DeletedAt;
                best = candidate;
            }
        }

        return best;
    }

    private static (string Path, DateTimeOffset DeletedAt)? ReadTrashInfo(string infoFile)
    {
        try
        {
            string? path = null;
            var deletedAt = DateTimeOffset.MinValue;

            foreach (var line in File.ReadLines(infoFile))
            {
                if (line.StartsWith("Path=", StringComparison.Ordinal))
                {
                    path = Uri.UnescapeDataString(line[5..]);
                }
                else if (line.StartsWith("DeletionDate=", StringComparison.Ordinal) &&
                         DateTime.TryParseExact(
                             line.AsSpan("DeletionDate=".Length),
                             "yyyy-MM-ddTHH:mm:ss",
                             CultureInfo.InvariantCulture,
                             DateTimeStyles.AssumeLocal,
                             out var when))
                {
                    deletedAt = new DateTimeOffset(when);
                }
            }

            return string.IsNullOrWhiteSpace(path) ? null : (path, deletedAt);
        }
        catch
        {
            return null;
        }
    }

    private void RemoveTrashInfo(string trashPath, string originalPath)
    {
        try
        {
            var direct = Path.Combine(InfoDirectory, Path.GetFileName(trashPath) + ".trashinfo");
            if (File.Exists(direct))
            {
                File.Delete(direct);
                return;
            }

            if (!Directory.Exists(InfoDirectory))
            {
                return;
            }

            foreach (var infoFile in Directory.EnumerateFiles(InfoDirectory, "*.trashinfo"))
            {
                var info = ReadTrashInfo(infoFile);
                if (info is not null && string.Equals(info.Value.Path, originalPath, StringComparison.Ordinal))
                {
                    File.Delete(infoFile);
                    return;
                }
            }
        }
        catch
        {
            // A stale .trashinfo only affects a later restore-by-path; the file is back.
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
