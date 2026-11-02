using AirClean.Core.Models;
using AirClean.Core.Scanning;
using AirClean.Core.Volumes;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class LargeFileScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "airclean-large", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Finds_files_above_the_threshold_sorted_by_size()
    {
        Write("small.bin", 10);
        Write(Path.Combine("nested", "medium.bin"), 40);
        Write("large.bin", 90);

        var result = await new LargeFileScanner(new TestTrash())
            .ScanAsync(Volume(_root), thresholdBytes: 20, progress: null, CancellationToken.None);

        var large = Assert.Single(result.Categories, category => category.Category == CleanCategory.LargeFiles);
        Assert.Equal(2, large.FileCount);
        Assert.Equal(130, large.Bytes);
        Assert.Equal(90, large.Items[0].Size);
        Assert.Equal(40, large.Items[1].Size);
        Assert.True(large.IsExplicitSelection);
        Assert.Equal(Safety.Caution, large.Safety);
    }

    [Fact]
    public async Task Reports_the_volume_trash_as_a_separate_permanent_row()
    {
        Write("large.bin", 90);
        var trash = new TestTrash { Sample = new(4096, 2) };

        var result = await new LargeFileScanner(trash)
            .ScanAsync(Volume(_root), thresholdBytes: 20, progress: null, CancellationToken.None);

        var trashRow = Assert.Single(result.Categories, category => category.Category == CleanCategory.Trash);
        Assert.True(trashRow.IsPermanent);
        Assert.Equal(4096, trashRow.Bytes);
        Assert.Equal(Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar, trashRow.TrashRoot);
    }

    [Fact]
    public async Task Uses_the_privileged_scanner_when_in_process_mft_is_unavailable()
    {
        var volume = Volume(_root);
        var item = new CleanItem(Path.Combine(_root, "from-helper.bin"), volume.Root, 40, DateTime.UtcNow);
        var privileged = new FakePrivilegedScanner(
            new LargeFileScanResult([item], MatchedBytes: 100, MatchedCount: 2));

        var result = await new LargeFileScanner(new TestTrash(), privileged)
            .ScanAsync(volume, thresholdBytes: 20, progress: null, CancellationToken.None);

        var large = Assert.Single(result.Categories, category => category.Category == CleanCategory.LargeFiles);
        Assert.Equal(1, privileged.Calls);
        Assert.Equal(100, large.MatchedBytes);
        Assert.Equal(2, large.MatchedCount);
        Assert.Single(large.Items);
        Assert.Equal(item.Path, large.Items[0].Path);
    }

    [Fact]
    public async Task Caps_the_list_at_two_hundred_entries()
    {
        for (var index = 0; index < 210; index++)
        {
            Write(Path.Combine("many", $"{index:000}.bin"), 30 + index);
        }

        var result = await new LargeFileScanner(new TestTrash())
            .ScanAsync(Volume(_root), thresholdBytes: 10, progress: null, CancellationToken.None);

        var large = Assert.Single(result.Categories, category => category.Category == CleanCategory.LargeFiles);
        Assert.Equal(LargeFileScanner.MaxItems, large.Items.Count);
    }

    private static VolumeInfo Volume(string root) =>
        new(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, "Test", "NTFS", 1_000_000, 500_000, false, false);

    private sealed class FakePrivilegedScanner(LargeFileScanResult result) : IPrivilegedLargeFileScanner
    {
        public int Calls { get; private set; }

        public LargeFileScanResult? TryScan(
            VolumeInfo volume,
            long thresholdBytes,
            IProgress<ScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            return result;
        }
    }

    private void Write(string relativePath, int size)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
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
