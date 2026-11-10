using AirClean.Core.Trash;
using AirClean.Platform;
using Xunit;

namespace AirClean.App.Tests;

/// <summary>
/// Exercises the real platform trash service. The recycle-bin cases only run on Windows;
/// the other platforms get their own round-trip when their restore path lands.
/// </summary>
public sealed class TrashRestoreTests
{
    [Fact]
    public void Windows_recycle_bin_moves_and_restores_a_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "airclean-recycle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var file = Path.Combine(root, "restore-me.bin");
            var content = new byte[] { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(file, content);

            var trash = PlatformServices.CreateTrashService();
            var moved = trash.MoveToTrash(file);

            Assert.True(moved.Success, moved.Error);
            Assert.False(File.Exists(file));

            Assert.True(trash.TryRestore(moved.TrashPath, file, out var error), error);

            Assert.True(File.Exists(file));
            Assert.Equal(content, File.ReadAllBytes(file));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    [Fact]
    public void Restoring_a_file_that_was_never_recycled_fails_with_a_reason()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var trash = PlatformServices.CreateTrashService();
        var missing = Path.Combine(Path.GetTempPath(), "airclean-recycle", Guid.NewGuid().ToString("N"), "missing.bin");

        Assert.False(trash.TryRestore(null, missing, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Linux_trash_moves_and_restores_a_file_with_the_reported_path()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = NewRoot();

        try
        {
            var file = Path.Combine(root, "restore-me.bin");
            var content = new byte[] { 9, 8, 7, 6 };
            File.WriteAllBytes(file, content);

            var trash = PlatformServices.CreateTrashService();
            var moved = trash.MoveToTrash(file);

            Assert.True(moved.Success, moved.Error);
            Assert.False(File.Exists(file));
            Assert.False(string.IsNullOrEmpty(moved.TrashPath));
            Assert.True(File.Exists(moved.TrashPath));

            Assert.True(trash.TryRestore(moved.TrashPath, file, out var error), error);

            Assert.True(File.Exists(file));
            Assert.Equal(content, File.ReadAllBytes(file));
            Assert.False(File.Exists(moved.TrashPath));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Linux_trash_resolves_the_item_by_its_original_path()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = NewRoot();

        try
        {
            var file = Path.Combine(root, "no-recorded-path.txt");
            File.WriteAllText(file, "hello");

            var trash = PlatformServices.CreateTrashService();
            Assert.True(trash.MoveToTrash(file).Success);

            // Null trash path forces the .trashinfo lookup, which is what a restart or a
            // batch written by an older version would use.
            Assert.True(trash.TryRestore(null, file, out var error), error);

            Assert.True(File.Exists(file));
            Assert.Equal("hello", File.ReadAllText(file));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Mac_trash_moves_and_restores_a_file_with_the_reported_path()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var root = NewRoot();

        try
        {
            var file = Path.Combine(root, "restore-me.bin");
            var content = new byte[] { 4, 3, 2, 1 };
            File.WriteAllBytes(file, content);

            var trash = PlatformServices.CreateTrashService();
            var moved = trash.MoveToTrash(file);

            Assert.True(moved.Success, moved.Error);
            Assert.False(File.Exists(file));
            Assert.False(string.IsNullOrEmpty(moved.TrashPath));

            Assert.True(trash.TryRestore(moved.TrashPath, file, out var error), error);

            Assert.True(File.Exists(file));
            Assert.Equal(content, File.ReadAllBytes(file));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "airclean-trash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
