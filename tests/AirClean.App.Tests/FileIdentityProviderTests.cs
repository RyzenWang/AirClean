using AirClean.Core.Security;
using AirClean.Platform.Identity;
using Xunit;

namespace AirClean.App.Tests;

/// <summary>
/// Exercises the shipping identity provider. The Core tests carry their own copy of the
/// FILE_ID_INFO call, so without this the P/Invoke that actually runs in the product would
/// never be executed by the test suite.
/// </summary>
public sealed class FileIdentityProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "airclean-identity",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_real_file_keeps_the_same_identity_across_reads()
    {
        var provider = FileIdentityProviders.Create();
        if (provider is NullFileIdentityProvider)
        {
            // No implementation for this platform: the deleted-check degrades to size/mtime.
            return;
        }

        var file = Write("a.bin", [1, 2, 3]);

        Assert.True(provider.TryGet(file, out var first));
        Assert.True(provider.TryGet(file, out var second));
        Assert.Equal(first, second);
        Assert.NotEqual(default, first);
    }

    [Fact]
    public void A_separate_copy_is_a_different_object()
    {
        var provider = FileIdentityProviders.Create();
        if (provider is NullFileIdentityProvider)
        {
            return;
        }

        var original = Write("a.bin", [4, 5, 6]);
        var copy = Write("b.bin", [4, 5, 6]);

        Assert.True(provider.TryGet(original, out var originalIdentity));
        Assert.True(provider.TryGet(copy, out var copyIdentity));

        // Same bytes, different object: this is exactly the case the delete check must not
        // confuse, because deleting the copy frees space while deleting a hard link does not.
        Assert.NotEqual(originalIdentity, copyIdentity);
    }

    [Fact]
    public void A_missing_path_reports_no_identity_instead_of_throwing()
    {
        var provider = FileIdentityProviders.Create();

        Assert.False(provider.TryGet(Path.Combine(_root, "missing.bin"), out _));
    }

    [Fact]
    public void A_directory_has_an_identity_as_well()
    {
        var provider = FileIdentityProviders.Create();
        if (provider is NullFileIdentityProvider)
        {
            return;
        }

        Directory.CreateDirectory(_root);

        Assert.True(provider.TryGet(_root, out var first));
        Assert.True(provider.TryGet(_root, out var second));
        Assert.Equal(first, second);
    }

    private string Write(string name, byte[] content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
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
            // A leaked temp directory must not fail the test run.
        }
    }
}
