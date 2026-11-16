using System.Runtime.InteropServices;
using AirClean.Core.Models;
using AirClean.Core.Scanning;
using AirClean.Core.Security;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace AirClean.Core.Tests;

/// <summary>
/// Uses real hard links. <see cref="DuplicateScanner"/> only folds them when an identity
/// provider is supplied; the product implementation lives in AirClean.Platform, which this
/// test project does not reference, so the small provider below performs the same
/// FILE_ID_INFO call. That keeps the test about genuine inode identity instead of a fake map.
///
/// Hard links are a Windows/NTFS feature to create through Win32 here; when the platform or
/// the volume refuses them the test returns early (a runtime skip) and says why.
/// </summary>
public sealed class HardlinkDedupTests : IDisposable
{
    private const int FileSize = 1_200_000;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "airclean-hardlinks", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Hard_links_are_folded_into_one_duplicate_entry()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Creating a hard link here is Win32-specific. Linux/macOS paths are covered by
            // the production provider, but this test does not pretend to run there.
            return;
        }

        var content = Pattern(0x21);
        var first = Write(Path.Combine("a", "movie.bin"), content);
        var link = Path.Combine(_root, "b", "movie-link.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        if (!CreateHardLink(link, first, IntPtr.Zero))
        {
            // FAT32/exFAT or a policy can refuse hard links; the scenario does not apply.
            return;
        }

        var separateCopy = Write(Path.Combine("c", "movie-copy.bin"), content);

        var result = await new DuplicateScanner(identityProvider: new TestWindowsIdentityProvider())
            .ScanAsync([_root], null, CancellationToken.None);

        var group = Assert.Single(result.DuplicateGroups!);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(FileSize, group.RemovableBytes);
        Assert.Equal(1, result.HardlinkDuplicatesCollapsed);

        var category = Assert.Single(result.Categories);
        Assert.Equal(1, category.FileCount);
        Assert.Equal(FileSize, category.Bytes);
        Assert.Equal(1, category.HardlinkDuplicatesCollapsed);

        // Exactly one of the two names for the shared data is offered; the separate copy is
        // the only path whose removal actually frees bytes.
        var paths = group.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(separateCopy, paths);
        Assert.True(paths.Contains(first) ^ paths.Contains(link));
        Assert.All(category.Items, item => Assert.NotNull(item.Identity));
    }

    [Fact]
    public async Task Without_identity_support_hard_links_keep_the_previous_behaviour()
    {
        if (!OperatingSystem.IsWindows())
        {
            // See the note on the first test: runtime skip instead of [Fact(Skip = ...)].
            return;
        }

        var content = Pattern(0x33);
        var first = Write(Path.Combine("a", "movie.bin"), content);
        var link = Path.Combine(_root, "b", "movie-link.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        if (!CreateHardLink(link, first, IntPtr.Zero))
        {
            return;
        }

        Write(Path.Combine("c", "movie-copy.bin"), content);

        var result = await new DuplicateScanner(identityProvider: NullFileIdentityProvider.Instance)
            .ScanAsync([_root], null, CancellationToken.None);

        // Unsupported providers must not change existing results: all three paths are still
        // considered duplicates, exactly as before identity support existed.
        var group = Assert.Single(result.DuplicateGroups!);
        Assert.Equal(3, group.Files.Count);
        Assert.Equal(2L * FileSize, group.RemovableBytes);
        Assert.Equal(0, result.HardlinkDuplicatesCollapsed);
    }

    private static byte[] Pattern(byte seed)
    {
        var content = new byte[FileSize];
        for (var index = 0; index < content.Length; index++)
        {
            content[index] = (byte)((index * 31 + seed) % 251);
        }

        return content;
    }

    private string Write(string relativePath, byte[] content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);

    /// <summary>Reads the same Windows identity the product provider reads.</summary>
    private sealed class TestWindowsIdentityProvider : IFileIdentityProvider
    {
        private const int FileIdInfoClass = 18;

        public bool TryGet(string path, out FileIdentity identity)
        {
            identity = default;

            try
            {
                using var handle = File.OpenHandle(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                if (!GetFileInformationByHandleEx(
                        handle,
                        FileIdInfoClass,
                        out var info,
                        (uint)Marshal.SizeOf<FileIdInfo>()))
                {
                    return false;
                }

                identity = new FileIdentity(info.VolumeSerialNumber, info.FileId.Low, info.FileId.High);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            int fileInformationClass,
            out FileIdInfo fileInformation,
            uint bufferSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdInfo
        {
            public ulong VolumeSerialNumber;

            public FileId128 FileId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileId128
        {
            public ulong Low;

            public ulong High;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
