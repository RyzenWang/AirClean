using AirClean.Core.Suggestions;
using Xunit;

namespace AirClean.Core.Tests;

/// <summary>
/// The apt cache probe is Linux-only in the product; the factory gates the platform so the
/// measurement logic can be tested everywhere.
/// </summary>
public sealed class LinuxPackageCacheProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "airclean-apt-cache",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Debian_packages_are_summed_and_other_files_ignored()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "a_1.0_amd64.deb"), new byte[1_200]);
        File.WriteAllBytes(Path.Combine(_root, "b_2.0_amd64.deb"), new byte[800]);
        File.WriteAllText(Path.Combine(_root, "lock"), "not a package");
        File.WriteAllText(Path.Combine(_root, "partial.deb.bak"), "not a package either");

        var suggestion = Assert.Single(new LinuxPackageCacheProbe(_root).Probe());

        Assert.Equal(2_000, suggestion.Bytes);
        Assert.Equal(LinuxPackageCacheProbe.Command, suggestion.SuggestedCommand);
    }

    [Fact]
    public void An_empty_cache_produces_nothing()
    {
        Directory.CreateDirectory(_root);

        Assert.Empty(new LinuxPackageCacheProbe(_root).Probe());
    }

    [Fact]
    public void A_missing_cache_produces_nothing_instead_of_zero()
    {
        Assert.Empty(new LinuxPackageCacheProbe(Path.Combine(_root, "not-here")).Probe());
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
