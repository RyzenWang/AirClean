namespace AirClean.Core.Security;

/// <summary>
/// Path rules that every scan and every delete goes through. The lists are hard-coded on
/// purpose: a bad rule must not be able to reach them.
///
/// Two levels exist because "protect user content" and "protect the operating system" are
/// different promises. Cache rules may never touch documents; an explicitly hand-picked
/// large file inside Documents is allowed, but it still may not be a system path.
/// </summary>
public static class PathGuard
{
    private static readonly StringComparison Comparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    private static readonly Lazy<string[]> ExactRoots = new(EnumerateExactRoots);
    private static readonly Lazy<string[]> SystemTrees = new(EnumerateSystemTrees);
    private static readonly Lazy<string[]> UserTrees = new(EnumerateUserTrees);

    public static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path;
        }
    }

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="root"/>.</summary>
    public static bool IsUnder(string root, string path)
    {
        var normalizedRoot = Normalize(root);
        var normalizedPath = Normalize(path);

        if (string.Equals(normalizedRoot, normalizedPath, Comparison))
        {
            return false;
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedPath);
        return relative != "." &&
               !relative.StartsWith("..", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    /// <summary>
    /// A path a cache rule may never delete or scan: drive roots, the home directory
    /// itself, operating-system trees and the user's documents.
    /// </summary>
    public static bool IsProtected(string path)
    {
        var normalized = Normalize(path);

        return IsExactRoot(normalized) ||
               IsInTree(SystemTrees.Value, normalized) ||
               IsInTree(UserTrees.Value, normalized);
    }

    /// <summary>
    /// The stricter floor for files the user picked by hand: system locations and drive
    /// roots stay off limits, but the user's own folders are fine.
    /// </summary>
    public static bool IsSystemProtected(string path)
    {
        var normalized = Normalize(path);
        return IsExactRoot(normalized) || IsInTree(SystemTrees.Value, normalized);
    }

    private static bool IsExactRoot(string normalized)
    {
        foreach (var root in ExactRoots.Value)
        {
            if (string.Equals(normalized, root, Comparison))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInTree(string[] trees, string normalized)
    {
        foreach (var tree in trees)
        {
            if (string.Equals(normalized, tree, Comparison) || IsUnder(tree, normalized))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] EnumerateExactRoots()
    {
        var roots = new List<string> { "/" };

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            roots.Add(Normalize(home));
        }

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    roots.Add(Normalize(drive.RootDirectory.FullName));
                }
            }
        }
        catch
        {
            // Drive enumeration can fail in restricted environments; the rest still applies.
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] EnumerateSystemTrees()
    {
        var trees = new List<string>();

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                Path.IsPathFullyQualified(path) &&
                Normalize(path).Length > 1)
            {
                trees.Add(Normalize(path));
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            // The whole Windows directory, not just System32: Installer keeps the MSI
            // cache that uninstall and repair need, Panther keeps setup logs.
            Add(windows);
            Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        }
        else
        {
            foreach (var path in new[] { "/usr", "/etc", "/bin", "/sbin", "/boot", "/dev", "/proc", "/sys", "/System", "/Library" })
            {
                Add(path);
            }
        }

        return trees.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] EnumerateUserTrees() => BuildUserTrees(
        [
            // Known folders rather than "home + English name": Windows lets these be
            // redirected (OneDrive "Backup > Documents" moves them without renaming), and
            // the scan roots already follow the redirect. Building this list by hand would
            // mean the app scans a redirected Documents folder but no longer protects it.
            (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents"),
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pictures"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Videos"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music")
        ],
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// Turns resolved known folders into the protected user trees. A known folder that does
    /// not resolve (on Unix the API returns empty until the folder exists) falls back to its
    /// canonical name under the home directory, so a folder created later is still covered.
    /// Internal so a test can prove a redirected folder with a non-English name is protected
    /// without redirecting the machine it runs on.
    /// </summary>
    internal static string[] BuildUserTrees(
        IReadOnlyList<(string? Known, string FallbackName)> folders,
        string? home)
    {
        var trees = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return;
            }

            var normalized = Normalize(path);
            if (normalized.Length > 1)
            {
                trees.Add(normalized);
            }
        }

        foreach (var (known, fallbackName) in folders)
        {
            if (!string.IsNullOrWhiteSpace(known))
            {
                Add(known);
            }
            else if (!string.IsNullOrWhiteSpace(home))
            {
                Add(Path.Combine(home, fallbackName));
            }
        }

        if (!string.IsNullOrWhiteSpace(home))
        {
            Add(Path.Combine(home, ".ssh"));
            Add(Path.Combine(home, ".gnupg"));
            Add(Path.Combine(home, ".config"));
        }

        // Downloads is deliberately absent: it is a scan root and the place users most often
        // want cleaned, and hand-picked files there already pass the stricter system check.
        var comparer = Comparison == StringComparison.Ordinal
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

        return trees.Distinct(comparer).ToArray();
    }
}
