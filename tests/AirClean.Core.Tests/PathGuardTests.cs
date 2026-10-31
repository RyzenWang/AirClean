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
