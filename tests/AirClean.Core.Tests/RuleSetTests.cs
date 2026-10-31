using System.Text;
using AirClean.Core.Rules;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class RuleSetTests
{
    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    [InlineData("linux")]
    public void Embedded_rule_sets_load_for_every_platform(string platform)
    {
        var ruleSet = RuleSet.LoadEmbedded(platform);

        Assert.NotEmpty(ruleSet.Rules);
        Assert.Contains(ruleSet.Rules, rule => rule.EmptyTrash);
        Assert.All(ruleSet.Rules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Source));
        });
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var json = """
        {
          "schemaVersion": 1,
          "platform": "test",
          "rules": [
            { "id": "a", "category": "SystemTemp", "safety": "Safe", "roots": ["/tmp"], "maxDepth": 2, "minAgeDays": 1 },
            { "id": "a", "category": "AppCache", "safety": "Safe", "roots": ["/tmp"], "maxDepth": 2, "minAgeDays": 1 }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            RuleSet.Load(new MemoryStream(Encoding.UTF8.GetBytes(json))));
    }

    [Fact]
    public void Unknown_category_is_rejected()
    {
        var json = """
        {
          "schemaVersion": 1,
          "platform": "test",
          "rules": [
            { "id": "a", "category": "Registry", "safety": "Safe", "roots": ["/tmp"], "maxDepth": 2, "minAgeDays": 1 }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            RuleSet.Load(new MemoryStream(Encoding.UTF8.GetBytes(json))));
    }

    [Fact]
    public void Out_of_range_depth_is_rejected()
    {
        var json = """
        {
          "schemaVersion": 1,
          "platform": "test",
          "rules": [
            { "id": "a", "category": "SystemTemp", "safety": "Safe", "roots": ["/tmp"], "maxDepth": 99, "minAgeDays": 1 }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            RuleSet.Load(new MemoryStream(Encoding.UTF8.GetBytes(json))));
    }
}
