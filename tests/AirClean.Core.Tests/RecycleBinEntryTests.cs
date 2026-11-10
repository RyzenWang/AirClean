using System.Buffers.Binary;
using System.Text;
using AirClean.Core.Platform.Windows;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class RecycleBinEntryTests
{
    private static readonly DateTime DeletedAt = new(2026, 11, 9, 12, 30, 45, DateTimeKind.Utc);

    [Fact]
    public void Version_one_entries_use_the_fixed_260_character_path()
    {
        var entry = RecycleBinEntry.TryParse(BuildVersionOne(@"C:\Users\me\Downloads\report.pdf", 4096));

        Assert.NotNull(entry);
        Assert.Equal(1, entry.Version);
        Assert.Equal(4096, entry.OriginalSize);
        Assert.Equal(@"C:\Users\me\Downloads\report.pdf", entry.OriginalPath);
        Assert.Equal(DeletedAt, entry.DeletedAt.UtcDateTime);
    }

    [Fact]
    public void Version_one_ignores_the_declared_length_and_still_reads_the_fixed_buffer()
    {
        var data = BuildVersionOne(@"C:\Temp\a.bin", 10);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 0);

        var entry = RecycleBinEntry.TryParse(data);

        Assert.NotNull(entry);
        Assert.Equal(@"C:\Temp\a.bin", entry.OriginalPath);
    }

    [Fact]
    public void Version_two_entries_read_the_variable_length_utf16_path()
    {
        var entry = RecycleBinEntry.TryParse(
            BuildVersionTwo(@"D:\项目\数据 备份\年度报告 2026.xlsx", 12_345));

        Assert.NotNull(entry);
        Assert.Equal(2, entry.Version);
        Assert.Equal(12_345, entry.OriginalSize);
        Assert.Equal(@"D:\项目\数据 备份\年度报告 2026.xlsx", entry.OriginalPath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Unknown_versions_are_rejected(int version)
    {
        var data = BuildVersionTwo(@"C:\Temp\a.bin", 1);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(0), version);

        Assert.Null(RecycleBinEntry.TryParse(data));
    }

    [Fact]
    public void Truncated_files_are_rejected()
    {
        Assert.Null(RecycleBinEntry.TryParse([]));
        Assert.Null(RecycleBinEntry.TryParse(new byte[20]));
        Assert.Null(RecycleBinEntry.TryParse(new byte[28]));
    }

    [Fact]
    public void A_path_that_is_not_fully_qualified_is_rejected()
    {
        Assert.Null(RecycleBinEntry.TryParse(BuildVersionTwo(@"relative\a.bin", 1)));
    }

    [Fact]
    public void An_empty_path_is_rejected()
    {
        Assert.Null(RecycleBinEntry.TryParse(BuildVersionTwo(string.Empty, 1)));
    }

    [Fact]
    public void An_invalid_deletion_time_is_rejected()
    {
        var data = BuildVersionTwo(@"C:\Temp\a.bin", 1);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), long.MaxValue);

        Assert.Null(RecycleBinEntry.TryParse(data));
    }

    [Fact]
    public void A_negative_original_size_is_rejected()
    {
        var data = BuildVersionTwo(@"C:\Temp\a.bin", -1);

        Assert.Null(RecycleBinEntry.TryParse(data));
    }

    private static byte[] BuildVersionOne(string path, long size)
    {
        var data = new byte[28 + (260 * 2)];
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(0), 1);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), DeletedAt.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 260);
        Encoding.Unicode.GetBytes(path, data.AsSpan(28));
        return data;
    }

    private static byte[] BuildVersionTwo(string path, long size)
    {
        var characters = path.Length + 1;
        var data = new byte[28 + (characters * 2) + 6];
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(0), 2);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), DeletedAt.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), characters);
        Encoding.Unicode.GetBytes(path, data.AsSpan(28));
        return data;
    }
}
