using AirClean.Core.Models;
using AirClean.Core.Scanning;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class DuplicateScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "airclean-dups", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Groups_identical_files_and_keeps_the_oldest()
    {
        var older = Write(Path.Combine("a", "movie.bin"), Pattern(0x11));
        var newer = Write(Path.Combine("b", "movie-copy.bin"), Pattern(0x11));
        Write(Path.Combine("c", "other.bin"), Pattern(0x77));

        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddDays(-1));

        var result = await new DuplicateScanner().ScanAsync([_root], null, CancellationToken.None);

        var group = Assert.Single(result.DuplicateGroups!);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(older, group.Keeper.Path);
        Assert.Single(group.Removable);

        var category = Assert.Single(result.Categories);
        Assert.Equal(CleanCategory.Duplicates, category.Category);
        Assert.Equal(Safety.Caution, category.Safety);
        Assert.Equal(1, category.FileCount);
        Assert.Equal(1_200_000, category.Bytes);
    }

    [Fact]
    public async Task Files_below_one_megabyte_are_ignored()
    {
        Write(Path.Combine("a", "tiny.bin"), new byte[512_000]);
        Write(Path.Combine("b", "tiny-copy.bin"), new byte[512_000]);

        var result = await new DuplicateScanner().ScanAsync([_root], null, CancellationToken.None);

        Assert.Empty(result.DuplicateGroups!);
    }

    [Fact]
    public async Task Same_size_but_different_content_is_not_a_duplicate()
    {
        Write(Path.Combine("a", "one.bin"), Pattern(0x01));
        Write(Path.Combine("b", "two.bin"), Pattern(0x02));

        var result = await new DuplicateScanner().ScanAsync([_root], null, CancellationToken.None);

        Assert.Empty(result.DuplicateGroups!);
    }

    private static byte[] Pattern(byte seed)
    {
        var content = new byte[1_200_000];
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
