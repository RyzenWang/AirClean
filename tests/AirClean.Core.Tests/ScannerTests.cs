using System.Text;
using AirClean.Core.Models;
using AirClean.Core.Rules;
using AirClean.Core.Scanning;
using AirClean.Core.Security;
using AirClean.Core.Trash;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class ScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "airclean-scan", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Scan_collects_old_files_and_honours_the_age_cutoff()
    {
        Write("old.tmp", 100, daysAgo: 5);
        Write(Path.Combine("nested", "deeper", "old.tmp"), 200, daysAgo: 5);
        Write("fresh.tmp", 999, daysAgo: 0);

        var scanner = new Scanner(BuildRules(minAgeDays: 1, maxDepth: 4), new TestTrash());
        var result = await scanner.ScanAsync(null, CancellationToken.None);

        var category = Assert.Single(result.Categories);
        Assert.Equal(2, category.FileCount);
        Assert.Equal(300, category.Bytes);
        Assert.Equal(CleanCategory.SystemTemp, category.Category);
    }

    [Fact]
    public async Task Scan_respects_max_depth()
    {
        Write("one.tmp", 10, daysAgo: 5);
        Write(Path.Combine("a", "two.tmp"), 20, daysAgo: 5);
        Write(Path.Combine("a", "b", "three.tmp"), 40, daysAgo: 5);

        var scanner = new Scanner(BuildRules(minAgeDays: 0, maxDepth: 1), new TestTrash());
        var result = await scanner.ScanAsync(null, CancellationToken.None);

        // Depth 0 is the root itself; depth 1 is "a". "a/b" is beyond the limit.
        Assert.Equal(2, result.Categories[0].FileCount);
    }

    [Fact]
    public async Task An_excluded_directory_is_not_scanned()
    {
        Write("keep.tmp", 10, daysAgo: 5);
        Write(Path.Combine("skip", "inside.tmp"), 20, daysAgo: 5);

        var scanner = new Scanner(
            BuildRules(minAgeDays: 0, maxDepth: 4),
            new TestTrash(),
            new ExclusionSet([Path.Combine(_root, "skip")]));

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        var item = Assert.Single(result.Categories[0].Items);
        Assert.EndsWith("keep.tmp", item.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlapping_roots_are_not_counted_twice()
    {
        Write("old.tmp", 100, daysAgo: 5);
        Write(Path.Combine("nested", "old.tmp"), 100, daysAgo: 5);

        var roots = $"[\"{Escape(_root)}\", \"{Escape(Path.Combine(_root, "nested"))}\"]";
        var scanner = new Scanner(LoadRules(roots, minAgeDays: 1, maxDepth: 4), new TestTrash());
        var result = await scanner.ScanAsync(null, CancellationToken.None);

        // The nested file is reachable from both roots but must only count once.
        Assert.Equal(2, result.Categories[0].FileCount);
        Assert.Equal(200, result.Categories[0].Bytes);
    }

    [Fact]
    public async Task Trash_rule_uses_the_trash_sample()
    {
        var trash = new TestTrash { Sample = new TrashSample(4_000, 12) };
        var scanner = new Scanner(LoadRules("[]", minAgeDays: 0, maxDepth: 2, emptyTrash: true), trash);

        var result = await scanner.ScanAsync(null, CancellationToken.None);

        Assert.Equal(4_000, result.Categories[0].Bytes);
        Assert.Equal(12, result.Categories[0].FileCount);
        Assert.True(result.Categories[0].IsPermanent);
    }

    [Fact]
    public async Task Cancellation_stops_the_scan()
    {
        for (var index = 0; index < 50; index++)
        {
            Write(Path.Combine("dir", index.ToString(), "file.tmp"), 10, daysAgo: 5);
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var scanner = new Scanner(BuildRules(minAgeDays: 0, maxDepth: 4), new TestTrash());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(null, cancellation.Token));
    }

    private RuleSet BuildRules(int minAgeDays, int maxDepth) =>
        LoadRules($"[\"{Escape(_root)}\"]", minAgeDays, maxDepth);

    private static RuleSet LoadRules(string roots, int minAgeDays, int maxDepth, bool emptyTrash = false)
    {
        var action = emptyTrash ? "empty-trash" : "contents";
        var json = $$"""
        {
          "schemaVersion": 1,
          "platform": "test",
          "rules": [
            {
              "id": "test.rule",
              "category": "SystemTemp",
              "safety": "Safe",
              "action": "{{action}}",
              "roots": {{roots}},
              "maxDepth": {{maxDepth}},
              "minAgeDays": {{minAgeDays}}
            }
          ]
        }
        """;

        return RuleSet.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");

    private void Write(string relativePath, int size, int daysAgo)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysAgo));
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
            // Test cleanup is best effort.
        }
    }

}
