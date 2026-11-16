using AirClean.Core.Suggestions;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class SuggestionScannerTests
{
    private const long Megabyte = 1024 * 1024;

    [Fact]
    public void A_known_large_suggestion_is_kept()
    {
        var scanner = new SuggestionScanner(
            [new FakeProbe(Entry("Hibernation file", 250 * Megabyte))]);

        var suggestion = Assert.Single(scanner.Scan());

        Assert.Equal("Hibernation file", suggestion.Label);
        Assert.Equal(250 * Megabyte, suggestion.Bytes);
    }

    [Fact]
    public void A_known_small_suggestion_is_dropped()
    {
        var scanner = new SuggestionScanner(
            [new FakeProbe(Entry("Small leftover", 100 * Megabyte))]);

        Assert.Empty(scanner.Scan());
    }

    [Fact]
    public void A_known_size_at_the_threshold_is_kept_and_one_byte_less_is_dropped()
    {
        var scanner = new SuggestionScanner(
        [
            new FakeProbe(
                Entry("Exactly at the threshold", SuggestionScanner.MinimumKnownBytes),
                Entry("One byte below", SuggestionScanner.MinimumKnownBytes - 1))
        ]);

        var suggestion = Assert.Single(scanner.Scan());

        Assert.Equal("Exactly at the threshold", suggestion.Label);
    }

    [Fact]
    public void A_suggestion_with_an_unknown_size_is_kept()
    {
        var scanner = new SuggestionScanner(
            [new FakeProbe(Entry("Component store", null, "DISM /Online /Cleanup-Image /AnalyzeComponentStore"))]);

        var suggestion = Assert.Single(scanner.Scan());

        Assert.Null(suggestion.Bytes);
        Assert.Equal("DISM /Online /Cleanup-Image /AnalyzeComponentStore", suggestion.SuggestedCommand);
    }

    [Fact]
    public void A_throwing_probe_does_not_hide_the_other_probes()
    {
        var scanner = new SuggestionScanner(
        [
            new ThrowingProbe(),
            new FakeProbe(Entry("Page file", 400 * Megabyte))
        ]);

        var suggestion = Assert.Single(scanner.Scan());

        Assert.Equal("Page file", suggestion.Label);
    }

    [Fact]
    public void Entries_already_yielded_survive_a_probe_that_fails_mid_enumeration()
    {
        var scanner = new SuggestionScanner(
        [
            new PartiallyThrowingProbe(Entry("Hibernation file", 300 * Megabyte)),
            new FakeProbe(Entry("Page file", 250 * Megabyte))
        ]);

        var suggestions = scanner.Scan();

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("Hibernation file", suggestions[0].Label);
        Assert.Equal("Page file", suggestions[1].Label);
    }

    [Fact]
    public void Known_sizes_sort_descending_and_unknown_sizes_go_last()
    {
        var scanner = new SuggestionScanner(
        [
            new FakeProbe(
                Entry("Unknown", null),
                Entry("Medium", 250 * Megabyte),
                Entry("Small enough to drop", 100 * Megabyte),
                Entry("Large", 900 * Megabyte))
        ]);

        var labels = scanner.Scan().Select(suggestion => suggestion.Label).ToArray();

        Assert.Equal(["Large", "Medium", "Unknown"], labels);
    }

    [Fact]
    public void The_same_label_from_two_probes_is_listed_once()
    {
        var scanner = new SuggestionScanner(
        [
            new FakeProbe(Entry("Page file", 300 * Megabyte)),
            new FakeProbe(Entry("Page file", 500 * Megabyte))
        ]);

        var suggestion = Assert.Single(scanner.Scan());

        Assert.Equal(300 * Megabyte, suggestion.Bytes);
    }

    private static ReadOnlySuggestion Entry(string label, long? bytes, string? command = null) =>
        new(label, bytes, "Why " + label + " stays", command);

    private sealed class FakeProbe(params ReadOnlySuggestion[] suggestions) : IReadOnlySuggestionProbe
    {
        public IEnumerable<ReadOnlySuggestion> Probe() => suggestions;
    }

    private sealed class ThrowingProbe : IReadOnlySuggestionProbe
    {
        public IEnumerable<ReadOnlySuggestion> Probe() =>
            throw new InvalidOperationException("This probe is not allowed to escape.");
    }

    private sealed class PartiallyThrowingProbe(ReadOnlySuggestion suggestion) : IReadOnlySuggestionProbe
    {
        public IEnumerable<ReadOnlySuggestion> Probe()
        {
            yield return suggestion;
            throw new IOException("The drive went away halfway through.");
        }
    }
}
