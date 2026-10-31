using AirClean.Core.Formatting;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0", "B")]
    [InlineData(999, "999", "B")]
    [InlineData(1_500, "1.5", "KB")]
    [InlineData(812_000_000, "812", "MB")]
    [InlineData(12_400_000_000, "12.4", "GB")]
    [InlineData(428_900_000_000, "429", "GB")]
    [InlineData(2_400_000_000_000, "2.4", "TB")]
    public void Split_uses_decimal_units_and_one_decimal_below_100(long bytes, string number, string unit)
    {
        var (actualNumber, actualUnit) = ByteSize.Split(bytes);

        Assert.Equal(number, actualNumber);
        Assert.Equal(unit, actualUnit);
    }

    [Fact]
    public void Negative_sizes_are_clamped_to_zero()
    {
        Assert.Equal("0 B", ByteSize.Format(-1));
    }
}
