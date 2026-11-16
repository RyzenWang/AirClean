using AirClean.Core.Suggestions;
using Xunit;

namespace AirClean.Core.Tests;

/// <summary>
/// The Full Disk Access probe is macOS-only in the product, but its decision logic is not:
/// the factory gates the platform and the probe itself is tested here on every OS.
/// </summary>
public sealed class MacOsPermissionProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "airclean-containers",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_denied_folder_is_reported_with_the_settings_command()
    {
        Directory.CreateDirectory(_root);

        var suggestions = new MacOsPermissionProbe(_root, _ => false).Probe().ToList();

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(MacOsPermissionProbe.Label, suggestion.Label);
        Assert.Null(suggestion.Bytes);
        Assert.Contains("Privacy_AllFiles", suggestion.SuggestedCommand ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_readable_folder_produces_nothing()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "com.example.app"), string.Empty);

        Assert.Empty(new MacOsPermissionProbe(_root, _ => true).Probe());
    }

    [Fact]
    public void A_folder_that_does_not_exist_is_not_a_permission_problem()
    {
        var missing = Path.Combine(_root, "not-installed");

        Assert.Empty(new MacOsPermissionProbe(missing, _ => false).Probe());
    }

    [Fact]
    public void A_throwing_probe_is_treated_as_denied_rather_than_ignored()
    {
        Directory.CreateDirectory(_root);

        var suggestions = new MacOsPermissionProbe(_root, _ => throw new UnauthorizedAccessException()).Probe();

        Assert.Single(suggestions);
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
