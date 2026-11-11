using AirClean.Core.Security;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class ExclusionSetTests
{
    [Fact]
    public void Exact_and_child_paths_are_excluded_but_siblings_are_not()
    {
        var root = Path.Combine(Path.GetTempPath(), "airclean-exclude");
        var set = new ExclusionSet([root + Path.DirectorySeparatorChar]);

        Assert.True(set.Contains(root));
        Assert.True(set.Contains(Path.Combine(root, "nested", "file.bin")));
        Assert.False(set.Contains(root + "-other"));
        Assert.False(set.Contains(Path.Combine(Path.GetTempPath(), "elsewhere")));
    }

    [Fact]
    public void Replace_swaps_the_whole_set()
    {
        var set = new ExclusionSet([Path.Combine(Path.GetTempPath(), "first")]);
        var second = Path.Combine(Path.GetTempPath(), "second");

        set.Replace([second]);

        Assert.False(set.Contains(Path.Combine(Path.GetTempPath(), "first")));
        Assert.True(set.Contains(Path.Combine(second, "child")));
    }

    [Fact]
    public void An_empty_set_excludes_nothing()
    {
        var set = new ExclusionSet();

        Assert.True(set.IsEmpty);
        Assert.False(set.Contains(Path.Combine(Path.GetTempPath(), "anything")));
    }
}
