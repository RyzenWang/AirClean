using System.Text;
using AirClean.Core.Scanning;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class DuplicateTruncationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "airclean-duplicate-cap",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Groups_beyond_the_cap_are_counted_but_not_listed()
    {
        Directory.CreateDirectory(_root);

        for (var group = 0; group < 3; group++)
        {
            var content = Encoding.UTF8.GetBytes(new string((char)('a' + group), 1024 * 1024));
            for (var copy = 0; copy < 2; copy++)
            {
                await File.WriteAllBytesAsync(Path.Combine(_root, $"g{group}-{copy}.bin"), content);
            }
        }

        var scanner = new DuplicateScanner(maxGroups: 2);
        var result = await scanner.ScanAsync([_root], 1_000_000_000, null, CancellationToken.None);

        Assert.True(result.Limited);
        Assert.True(result.LimitedByGroupCap);
        Assert.Equal(3, result.DuplicateGroupCount);
        Assert.Equal(2, result.DuplicateGroups!.Count);

        var category = Assert.Single(result.Categories);
        Assert.Equal(3, category.MatchedCount);
        Assert.Equal(3L * 1024 * 1024, category.MatchedBytes);
    }

    [Fact]
    public async Task A_scan_below_the_cap_is_not_marked_limited()
    {
        Directory.CreateDirectory(_root);
        var content = Encoding.UTF8.GetBytes(new string('z', 1024 * 1024));
        await File.WriteAllBytesAsync(Path.Combine(_root, "one.bin"), content);
        await File.WriteAllBytesAsync(Path.Combine(_root, "two.bin"), content);

        var result = await new DuplicateScanner(maxGroups: 2)
            .ScanAsync([_root], 1_000_000_000, null, CancellationToken.None);

        Assert.False(result.Limited);
        Assert.False(result.LimitedByGroupCap);
        Assert.Equal(1, result.DuplicateGroupCount);
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
