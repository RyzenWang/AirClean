using AirClean.Core.Models;
using AirClean.Core.Scanning;
using AirClean.Core.Security;
using AirClean.Core.Volumes;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class FileIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "airclean-identity", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Null_provider_reports_no_identity_and_never_throws()
    {
        var file = Write("file.bin", 16);
        var provider = NullFileIdentityProvider.Instance;

        Assert.False(provider.TryGet(file, out var identity));
        Assert.Equal(default, identity);
        Assert.False(provider.TryGet(Path.Combine(_root, "missing.bin"), out _));
    }

    [Fact]
    public void Identity_equality_covers_volume_id_and_both_id_halves()
    {
        var identity = new FileIdentity(1, 2);
        var same = new FileIdentity(1, 2, 0);
        var otherVolume = new FileIdentity(2, 2);
        var otherHigh = new FileIdentity(1, 2, 1);
        var otherId = new FileIdentity(1, 3);

        Assert.Equal(identity, same);
        Assert.Equal(identity.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(identity, otherVolume);
        Assert.NotEqual(identity, otherHigh);
        Assert.NotEqual(identity, otherId);
    }

    [Fact]
    public async Task Large_file_scan_attaches_the_identity_to_offered_items()
    {
        Write("large.bin", 128);
        var identity = new FileIdentity(11, 22);
        var provider = new FixedIdentityProvider(identity);

        var result = await new LargeFileScanner(new TestTrash(), identityProvider: provider)
            .ScanAsync(Volume(_root), thresholdBytes: 16, progress: null, CancellationToken.None);

        var category = Assert.Single(result.Categories, category => category.Category == CleanCategory.LargeFiles);
        var item = Assert.Single(category.Items);
        Assert.Equal(identity, item.Identity);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Large_file_scan_leaves_identity_unset_when_the_provider_is_unsupported()
    {
        Write("large.bin", 128);

        var result = await new LargeFileScanner(new TestTrash(), identityProvider: NullFileIdentityProvider.Instance)
            .ScanAsync(Volume(_root), thresholdBytes: 16, progress: null, CancellationToken.None);

        var category = Assert.Single(result.Categories, category => category.Category == CleanCategory.LargeFiles);
        var item = Assert.Single(category.Items);
        Assert.Null(item.Identity);
    }

    private static VolumeInfo Volume(string root) =>
        new(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, "Test", "NTFS", 1_000_000, 500_000, false, false);

    private string Write(string relativePath, int size)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    private sealed class FixedIdentityProvider(FileIdentity identity) : IFileIdentityProvider
    {
        public int Calls { get; private set; }

        public bool TryGet(string path, out FileIdentity value)
        {
            Calls++;
            value = identity;
            return true;
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
