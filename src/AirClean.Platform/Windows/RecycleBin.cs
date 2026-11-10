using System.Runtime.Versioning;
using System.Security.Principal;
using AirClean.Core.Platform.Windows;

namespace AirClean.Platform.Windows;

/// <summary>
/// Finds an item in the current user's recycle bin by its original path and moves it back.
/// Windows' shell delete API does not report the recycle-bin path, so the journal stores
/// only the original path and this class does the lookup. A missing or unreadable recycle
/// bin is a normal outcome, not an exception.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RecycleBin
{
    public static bool TryRestore(string? trashPath, string originalPath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(originalPath))
        {
            error = "The original path is missing.";
            return false;
        }

        try
        {
            var candidate = !string.IsNullOrWhiteSpace(trashPath) &&
                            (File.Exists(trashPath) || Directory.Exists(trashPath))
                ? new Candidate(null, trashPath, null)
                : Find(originalPath);

            if (candidate is null)
            {
                error = "The item is no longer in the recycle bin.";
                return false;
            }

            if (!File.Exists(candidate.TrashPath) && !Directory.Exists(candidate.TrashPath))
            {
                error = "The item is no longer in the recycle bin.";
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

            if (Directory.Exists(candidate.TrashPath))
            {
                Directory.Move(Extended(candidate.TrashPath), Extended(originalPath));
            }
            else
            {
                File.Move(Extended(candidate.TrashPath), Extended(originalPath));
            }

            // A stale $I entry only wastes space; failing to remove it must not undo the
            // restore the user just got.
            if (candidate.InfoFile is { } infoFile)
            {
                try
                {
                    File.SetAttributes(infoFile, FileAttributes.Normal);
                    File.Delete(infoFile);
                }
                catch
                {
                    // Best effort.
                }
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

    private static Candidate? Find(string originalPath)
    {
        var volumeRoot = Path.GetPathRoot(originalPath);
        if (string.IsNullOrEmpty(volumeRoot))
        {
            return null;
        }

        var sid = CurrentUserSid();
        if (string.IsNullOrEmpty(sid))
        {
            return null;
        }

        var bin = Path.Combine(volumeRoot, "$Recycle.Bin", sid);
        if (!Directory.Exists(bin))
        {
            return null;
        }

        Candidate? best = null;

        foreach (var infoFile in Directory.EnumerateFiles(bin, "$I*"))
        {
            var entry = RecycleBinEntry.TryParseFile(infoFile);
            if (entry is null ||
                !string.Equals(entry.OriginalPath, originalPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Path.GetFileName(infoFile);
            var itemFile = Path.Combine(bin, "$R" + name[2..]);

            if (!File.Exists(itemFile) && !Directory.Exists(itemFile))
            {
                continue;
            }

            if (best is null || entry.DeletedAt > best.Entry!.DeletedAt)
            {
                best = new Candidate(infoFile, itemFile, entry);
            }
        }

        return best;
    }

    private static string? CurrentUserSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Long paths need the extended prefix; plain paths are left untouched.</summary>
    private static string Extended(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;

    private sealed record Candidate(string? InfoFile, string TrashPath, RecycleBinEntry? Entry);
}
