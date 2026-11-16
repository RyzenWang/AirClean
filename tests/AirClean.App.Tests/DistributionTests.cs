using AirClean.Platform;
using Xunit;

namespace AirClean.App.Tests;

/// <summary>
/// The packaged-build detection decides whether the app checks GitHub for updates and
/// whether it offers administrator acceleration. A test run is never packaged, so this
/// proves the probe answers cleanly instead of throwing, and that the default is "ordinary
/// build"; the packaged branch can only be exercised by an installed MSIX.
/// </summary>
public sealed class DistributionTests
{
    [Fact]
    public void An_unpackaged_build_is_not_store_managed()
    {
        Assert.False(PlatformServices.IsStoreManaged);
    }
}
