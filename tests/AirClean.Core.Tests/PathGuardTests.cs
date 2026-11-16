using AirClean.Core.Security;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class PathGuardTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "airclean-guard");

    [Fact]
    public void IsUnder_accepts_nested_paths_only()
    {
        Assert.True(PathGuard.IsUnder(Root, Path.Combine(Root, "a", "b.tmp")));
        Assert.False(PathGuard.IsUnder(Root, Root));
        Assert.False(PathGuard.IsUnder(Root, Path.Combine(Root, "..", "escape.tmp")));
        Assert.False(PathGuard.IsUnder(Root, Path.Combine(Path.GetTempPath(), "other")));
    }

    [Fact]
    public void Home_is_only_protected_as_itself()
    {
        Assert.True(PathGuard.IsProtected(Home));
        Assert.False(PathGuard.IsProtected(Path.Combine(Home, ".cache", "thing")));
    }

    [Fact]
    public void Documents_and_desktop_are_protected_subtrees()
    {
        Assert.True(PathGuard.IsProtected(Path.Combine(Home, "Documents")));
        Assert.True(PathGuard.IsProtected(Path.Combine(Home, "Documents", "notes.txt")));
        Assert.True(PathGuard.IsProtected(Path.Combine(Home, ".ssh", "id_ed25519")));
    }

    [Fact]
    public void Every_known_user_folder_is_protected()
    {
        // Mirrors the guard: a resolved known folder is protected under its real name, and a
        // folder that does not exist yet keeps its platform's canonical name. macOS calls the
        // video folder "Movies", so the expectation has to follow the platform too.
        var pairs = new (string? Known, string Fallback, string Name)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents", "Documents"),
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop", "Desktop"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pictures", "Pictures"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), OperatingSystem.IsMacOS() ? "Movies" : "Videos", "Videos"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music", "Music")
        };

        foreach (var (known, fallback, name) in pairs)
        {
            var path = string.IsNullOrWhiteSpace(known) ? Path.Combine(Home, fallback) : known;

            Assert.True(PathGuard.IsProtected(path), $"{name} is not protected ({path})");
            Assert.True(PathGuard.IsProtected(Path.Combine(path, "nested", "file.bin")), $"{name} children are not protected");
        }
    }

    [Fact]
    public void Documents_follow_the_redirected_known_folder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            return;
        }

        // The guard must agree with the scan root, whatever the folder is called on disk.
        Assert.Equal(Path.GetFullPath(documents).TrimEnd(Path.DirectorySeparatorChar), PathGuard.Normalize(documents));
        Assert.True(PathGuard.IsProtected(Path.Combine(documents, "report.docx")));
        Assert.False(PathGuard.IsSystemProtected(Path.Combine(documents, "report.docx")));
    }

    [Fact]
    public void A_redirected_documents_folder_is_protected_whatever_its_name()
    {
        var home = Path.Combine(Path.GetTempPath(), "airclean-home");
        var redirected = Path.Combine(Path.GetTempPath(), "OneDrive", "文档");

        var trees = PathGuard.BuildUserTrees([(redirected, "Documents"), (null, "Desktop")], home);

        // A redirected folder is protected under the name the OS reports, the guard must not
        // also invent "home\Documents", and a folder that did not resolve keeps its
        // canonical name so it is covered once it exists.
        Assert.Contains(trees, tree => tree == PathGuard.Normalize(redirected));
        Assert.DoesNotContain(trees, tree => tree == PathGuard.Normalize(Path.Combine(home, "Documents")));
        Assert.Contains(trees, tree => tree == PathGuard.Normalize(Path.Combine(home, "Desktop")));
        Assert.Contains(trees, tree => tree == PathGuard.Normalize(Path.Combine(home, ".ssh")));
    }

    [Fact]
    public void A_drive_or_filesystem_root_is_never_a_valid_target()
    {
        var root = Path.GetPathRoot(Home)!;
        Assert.True(PathGuard.IsProtected(root));
    }

    [Fact]
    public void The_windows_directory_is_off_limits_even_for_hand_picked_files()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // C:\Windows\Installer holds the MSI cache uninstall needs; Panther holds setup logs.
        Assert.True(PathGuard.IsSystemProtected(Path.Combine(windows, "Installer", "cache.msi")));
        Assert.True(PathGuard.IsSystemProtected(Path.Combine(windows, "Panther", "setupact.log")));
        Assert.True(PathGuard.IsSystemProtected(Path.Combine(windows, "System32", "kernel32.dll")));
    }

    [Fact]
    public void Hand_picked_files_may_live_in_user_folders()
    {
        Assert.False(PathGuard.IsSystemProtected(Path.Combine(Home, "Downloads", "big.iso")));
        Assert.False(PathGuard.IsSystemProtected(Path.Combine(Home, "Documents", "movie.mkv")));

        // The cache-rule guard is still stricter.
        Assert.True(PathGuard.IsProtected(Path.Combine(Home, "Documents", "movie.mkv")));
    }
}
