using System.Buffers.Binary;
using System.Text;
using AirClean.Core.Models;
using AirClean.Core.Scanning.Mft;
using AirClean.Core.Volumes;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class MftScannerTests
{
    private static readonly DateTime LastWrite = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void Parses_file_name_size_and_last_write_time()
    {
        var record = new RecordBuilder(42, isDirectory: false)
            .WithStandardInformation(LastWrite)
            .WithFileName(5, "movie.mp4")
            .WithResidentData(150)
            .Build();

        Assert.True(MftRecordParser.TryParse(record, 512, out var entry));
        Assert.NotNull(entry);
        Assert.Equal(42u, entry.RecordNumber);
        Assert.Equal(5u, entry.ParentRecord);
        Assert.Equal("movie.mp4", entry.Name);
        Assert.False(entry.IsDirectory);
        Assert.False(entry.IsExtension);
        Assert.True(entry.InUse);
        Assert.Equal(150, entry.Size);
        Assert.Equal(LastWrite, entry.LastWriteUtc);
    }

    [Fact]
    public void Uses_the_file_name_size_when_the_data_attribute_lives_elsewhere()
    {
        var record = new RecordBuilder(43, isDirectory: false)
            .WithFileName(5, "big.iso", realSize: 123_456)
            .Build();

        Assert.True(MftRecordParser.TryParse(record, 512, out var entry));
        Assert.NotNull(entry);
        Assert.Equal(123_456, entry.Size);
    }

    [Fact]
    public void Parses_the_mft_data_runs_from_record_zero()
    {
        // 1 cluster at LCN 0, then 2 clusters 10 clusters later.
        var runList = new byte[] { 0x11, 0x01, 0x00, 0x11, 0x02, 0x0A, 0x00 };
        var record = new RecordBuilder(0, isDirectory: false)
            .WithFileName(5, "$MFT")
            .WithNonResidentData(3 * 1024, runList)
            .Build();

        Assert.True(MftRecordParser.TryParse(record, 512, out var entry));
        Assert.NotNull(entry);

        var runs = Assert.IsType<MftDataRun[]>(entry.DataRuns);
        Assert.Equal(2, runs.Length);
        Assert.Equal(new MftDataRun(0, 1, 0), runs[0]);
        Assert.Equal(new MftDataRun(1, 2, 10), runs[1]);
    }

    [Fact]
    public void Resolves_paths_and_filters_protected_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "airclean-mft-index", Guid.NewGuid().ToString("N"));
        var volume = new VolumeInfo(
            Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            "Test",
            "NTFS",
            1_000_000,
            500_000,
            false,
            false);

        var entries = new Dictionary<uint, MftEntry>
        {
            [5] = Entry(5, 5, ".", isDirectory: true),
            [16] = Entry(16, 5, "Users", isDirectory: true),
            [17] = Entry(17, 16, "Sam", isDirectory: true),
            [18] = Entry(18, 17, "movie.mp4", size: 200_000_000),
            [19] = Entry(19, 5, "keep.bin", size: 150_000_000),
            [20] = Entry(20, 5, "pagefile.sys", size: 300_000_000),
            [21] = Entry(21, 5, "$Recycle.Bin", isDirectory: true),
            [22] = Entry(22, 21, "old.bin", size: 400_000_000),
            [23] = Entry(23, 5, "Junction", isDirectory: true, isReparsePoint: true),
            [24] = Entry(24, 23, "inside.bin", size: 500_000_000)
        };

        var result = new MftIndex(entries).FindLargeFiles(volume, 100_000_000, 200, CancellationToken.None);

        Assert.Equal(2, result.MatchedCount);
        Assert.Equal(350_000_000, result.MatchedBytes);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("movie.mp4", Path.GetFileName(result.Items[0].Path));
        Assert.Equal("keep.bin", Path.GetFileName(result.Items[1].Path));
        Assert.Equal(Path.Combine(root, "Users", "Sam", "movie.mp4"), result.Items[0].Path);
    }

    [Fact]
    public void Caps_the_index_but_keeps_the_matched_totals()
    {
        var root = Path.Combine(Path.GetTempPath(), "airclean-mft-cap", Guid.NewGuid().ToString("N"));
        var volume = new VolumeInfo(
            Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            "Test",
            "NTFS",
            1_000_000_000,
            500_000_000,
            false,
            false);

        var entries = new Dictionary<uint, MftEntry>
        {
            [5] = Entry(5, 5, ".", isDirectory: true)
        };

        for (uint index = 0; index < 205; index++)
        {
            entries[16 + index] = Entry(16 + index, 5, $"file-{index}.bin", size: 100_000_000);
        }

        var result = new MftIndex(entries).FindLargeFiles(volume, 50_000_000, 200, CancellationToken.None);

        Assert.Equal(205, result.MatchedCount);
        Assert.Equal(20_500_000_000, result.MatchedBytes);
        Assert.Equal(200, result.Items.Count);
    }

    private static MftEntry Entry(
        uint recordNumber,
        uint parent,
        string name,
        bool isDirectory = false,
        bool isReparsePoint = false,
        long size = 0) =>
        new()
        {
            RecordNumber = recordNumber,
            ParentRecord = parent,
            Name = name,
            IsDirectory = isDirectory,
            IsReparsePoint = isReparsePoint,
            InUse = true,
            IsExtension = false,
            Size = size,
            LastWriteUtc = LastWrite
        };

    /// <summary>Small, intentionally boring FILE record builder for parser tests.</summary>
    private sealed class RecordBuilder
    {
        private readonly byte[] _record = new byte[1024];
        private int _offset = 0x38;

        public RecordBuilder(uint recordNumber, bool isDirectory)
        {
            "FILE"u8.CopyTo(_record);
            WriteUInt16(0x30, 0x04); // update sequence array offset
            WriteUInt16(0x03, 0x06); // USN + two sector trailers
            WriteUInt16(0x38, 0x14); // attribute offset
            WriteUInt16((ushort)((isDirectory ? 0x0002 : 0) | 0x0001), 0x16);
            WriteUInt32(recordNumber, 0x2C);
        }

        public RecordBuilder WithStandardInformation(DateTime lastWrite)
        {
            var value = new byte[72];
            BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(0x08), lastWrite.ToFileTimeUtc());
            BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(0x20), 0x00000020);

            return AddResident(0x10, value);
        }

        public RecordBuilder WithFileName(uint parent, string name, byte nameSpace = 1, long realSize = 0)
        {
            var bytes = Encoding.Unicode.GetBytes(name);
            var value = new byte[0x42 + bytes.Length];
            BinaryPrimitives.WriteUInt64LittleEndian(value.AsSpan(0x00), parent);
            BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(0x10), LastWrite.ToFileTimeUtc());
            BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(0x30), realSize);
            value[0x40] = (byte)name.Length;
            value[0x41] = nameSpace;
            bytes.CopyTo(value, 0x42);

            return AddResident(0x30, value);
        }

        public RecordBuilder WithResidentData(int size)
        {
            return AddResident(0x80, new byte[size]);
        }

        public RecordBuilder WithNonResidentData(long size, byte[] runList)
        {
            _offset = Align(_offset);
            var length = Align(0x40 + runList.Length);

            WriteUInt32(0x80, _offset);
            WriteUInt32((uint)length, _offset + 0x04);
            _record[_offset + 0x08] = 1; // non-resident
            BinaryPrimitives.WriteUInt16LittleEndian(_record.AsSpan(_offset + 0x20), 0x40);
            BinaryPrimitives.WriteInt64LittleEndian(_record.AsSpan(_offset + 0x28), size);
            BinaryPrimitives.WriteInt64LittleEndian(_record.AsSpan(_offset + 0x30), size);
            BinaryPrimitives.WriteInt64LittleEndian(_record.AsSpan(_offset + 0x38), size);
            runList.CopyTo(_record, _offset + 0x40);

            _offset += length;
            return this;
        }

        public byte[] Build()
        {
            _offset = Align(_offset);
            WriteUInt32(0xFFFFFFFF, _offset);
            _offset += 4;
            WriteUInt32((uint)_offset, 0x18);

            const ushort updateSequenceNumber = 0xAAAA;
            WriteUInt16(updateSequenceNumber, 0x30);

            for (var sector = 0; sector < 2; sector++)
            {
                var trailer = (sector + 1) * 512 - 2;
                WriteUInt16(ReadUInt16(trailer), 0x32 + sector * 2);
                WriteUInt16(updateSequenceNumber, trailer);
            }

            return _record;
        }

        private RecordBuilder AddResident(uint type, byte[] value)
        {
            _offset = Align(_offset);
            var length = Align(0x18 + value.Length);

            WriteUInt32(type, _offset);
            WriteUInt32((uint)length, _offset + 0x04);
            BinaryPrimitives.WriteUInt32LittleEndian(_record.AsSpan(_offset + 0x10), (uint)value.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(_record.AsSpan(_offset + 0x14), 0x18);
            value.CopyTo(_record, _offset + 0x18);

            _offset += length;
            return this;
        }

        private void WriteUInt16(ushort value, int offset) =>
            BinaryPrimitives.WriteUInt16LittleEndian(_record.AsSpan(offset), value);

        private void WriteUInt32(uint value, int offset) =>
            BinaryPrimitives.WriteUInt32LittleEndian(_record.AsSpan(offset), value);

        private ushort ReadUInt16(int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(_record.AsSpan(offset));

        private static int Align(int value) => (value + 7) & ~7;
    }
}
