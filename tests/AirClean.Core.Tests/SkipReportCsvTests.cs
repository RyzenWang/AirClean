using AirClean.Core.Deleting;
using AirClean.Core.Models;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class SkipReportCsvTests
{
    [Fact]
    public void Writes_a_header_and_escapes_only_when_needed()
    {
        var at = new DateTimeOffset(2026, 11, 10, 5, 0, 0, TimeSpan.Zero);

        var csv = SkipReportCsv.Build(
            [
                new SkippedItem(@"C:\Temp\plain.bin", SkipReason.InUse),
                new SkippedItem(@"C:\Temp\a,b.bin", SkipReason.Permission),
                new SkippedItem("C:\\Temp\\quote\"x.bin", SkipReason.NotFound),
                new SkippedItem(@"C:\Temp\中文.bin", SkipReason.Protected)
            ],
            at);

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("path,reason,at", lines[0]);
        Assert.Equal(@"C:\Temp\plain.bin,InUse,2026-11-10T05:00:00.0000000+00:00", lines[1]);
        Assert.Equal(@"""C:\Temp\a,b.bin"",Permission,2026-11-10T05:00:00.0000000+00:00", lines[2]);
        Assert.Equal(@"""C:\Temp\quote""""x.bin"",NotFound,2026-11-10T05:00:00.0000000+00:00", lines[3]);
        Assert.Equal(@"C:\Temp\中文.bin,Protected,2026-11-10T05:00:00.0000000+00:00", lines[4]);
    }
}
