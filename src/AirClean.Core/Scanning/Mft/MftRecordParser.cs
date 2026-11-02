using System.Buffers.Binary;
using System.Text;

namespace AirClean.Core.Scanning.Mft;

/// <summary>
/// Parses the on-disk NTFS FILE record. Only the fields needed by the large-file index are
/// decoded: the primary Win32 file name, the unnamed $DATA size, the last-write stamp, and
/// (for record 0) the MFT's own data runs.
/// </summary>
internal static class MftRecordParser
{
    private const uint EndMarker = 0xFFFFFFFF;
    private const uint StandardInformation = 0x10;
    private const uint AttributeList = 0x20;
    private const uint FileName = 0x30;
    private const uint Data = 0x80;
    private const uint ReparsePointFlag = 0x400;

    public static bool TryParse(Span<byte> record, int bytesPerSector, out MftEntry? entry)
    {
        entry = null;

        if (record.Length < 0x30 ||
            bytesPerSector < 512 ||
            record.Length % bytesPerSector != 0 ||
            !ApplyFixup(record, bytesPerSector) ||
            !record[..4].SequenceEqual("FILE"u8))
        {
            return false;
        }

        var flags = ReadUInt16(record, 0x16);
        var recordNumber = ReadUInt32(record, 0x2C);
        var baseRecord = ReadInt64(record, 0x20);
        var attrsOffset = ReadUInt16(record, 0x14);
        var bytesInUse = ReadUInt32(record, 0x18);
        var limit = (int)Math.Min(record.Length, bytesInUse == 0 ? record.Length : bytesInUse);

        if (attrsOffset < 0x30 || attrsOffset >= limit)
        {
            return false;
        }

        uint parentRecord = 0;
        string? name = null;
        var bestNameRank = int.MaxValue;
        var isReparsePoint = false;
        var lastWriteUtc = DateTime.MinValue;
        long size = 0;
        long fileNameSize = 0;
        var hasData = false;
        MftDataRun[]? dataRuns = null;

        var cursor = (int)attrsOffset;

        while (cursor + 4 <= limit)
        {
            var type = ReadUInt32(record, cursor);

            if (type == EndMarker)
            {
                break;
            }

            if (cursor + 0x10 > limit)
            {
                return false;
            }

            var length = ReadUInt32(record, cursor + 0x04);

            if (length < 0x10 || length > (uint)(limit - cursor))
            {
                return false;
            }

            var attributeEnd = cursor + (int)length;
            var nonResident = record[cursor + 0x08] != 0;
            var attributeNameLength = record[cursor + 0x09];

            if (type == StandardInformation && !nonResident)
            {
                if (TryGetResidentValue(record, cursor, attributeEnd, out var valueStart, out var valueLength) &&
                    valueLength >= 0x24)
                {
                    lastWriteUtc = FileTime(record, valueStart + 0x08);

                    var attributes = ReadUInt32(record, valueStart + 0x20);
                    isReparsePoint |= (attributes & ReparsePointFlag) != 0;
                }
            }
            else if (type == FileName && !nonResident)
            {
                if (TryGetResidentValue(record, cursor, attributeEnd, out var valueStart, out var valueLength) &&
                    valueLength >= 0x42)
                {
                    var nameLength = record[valueStart + 0x40];
                    var nameSpace = record[valueStart + 0x41];
                    var nameStart = valueStart + 0x42;

                    if (nameLength is > 0 and <= 255 &&
                        nameStart + nameLength * 2 <= valueStart + valueLength)
                    {
                        var rank = nameSpace switch
                        {
                            1 or 3 => 0,
                            0 => 1,
                            _ => 2
                        };

                        if (rank < bestNameRank)
                        {
                            var candidate = Encoding.Unicode.GetString(record.Slice(nameStart, nameLength * 2));

                            if (!string.IsNullOrEmpty(candidate))
                            {
                                bestNameRank = rank;
                                name = candidate;
                                parentRecord = (uint)(ReadUInt64(record, valueStart) & 0x0000FFFFFFFFFFFF);
                                fileNameSize = Math.Max(0, ReadInt64(record, valueStart + 0x30));
                                isReparsePoint |= (ReadUInt32(record, valueStart + 0x38) & ReparsePointFlag) != 0;

                                if (lastWriteUtc == DateTime.MinValue)
                                {
                                    lastWriteUtc = FileTime(record, valueStart + 0x10);
                                }
                            }
                        }
                    }
                }
            }
            else if (type == Data && attributeNameLength == 0)
            {
                hasData = true;

                if (nonResident)
                {
                    size = ReadInt64(record, cursor + 0x30);

                    if (size < 0)
                    {
                        size = 0;
                    }

                    if (recordNumber == 0)
                    {
                        var runListOffset = ReadUInt16(record, cursor + 0x20);

                        if (runListOffset < length &&
                            MftDataRun.TryParse(record, cursor + runListOffset, attributeEnd, out var runs))
                        {
                            dataRuns = runs;
                        }
                    }
                }
                else if (TryGetResidentValue(record, cursor, attributeEnd, out _, out var valueLength))
                {
                    size = valueLength;
                }
            }
            else if (type == AttributeList && !nonResident)
            {
                // An attribute list can move a large $DATA attribute into an extension
                // record. Those files are rare; skipping the list keeps the hot loop cheap.
            }

            cursor = attributeEnd;
        }

        if (name is null && recordNumber != 0)
        {
            return false;
        }

        // A highly fragmented file can move its $DATA attribute into an extension record.
        // The chosen $FILE_NAME carries the same logical size, so it is a safe fallback
        // that keeps those files in the index without following attribute lists.
        if (!hasData && fileNameSize > 0)
        {
            size = fileNameSize;
        }

        entry = new MftEntry
        {
            RecordNumber = recordNumber,
            ParentRecord = parentRecord,
            Name = name ?? string.Empty,
            IsDirectory = (flags & 0x0002) != 0,
            IsReparsePoint = isReparsePoint,
            InUse = (flags & 0x0001) != 0,
            IsExtension = baseRecord != 0,
            Size = size,
            LastWriteUtc = lastWriteUtc,
            DataRuns = dataRuns
        };

        return true;
    }

    private static bool ApplyFixup(Span<byte> record, int bytesPerSector)
    {
        var usaOffset = ReadUInt16(record, 0x04);
        var usaCount = ReadUInt16(record, 0x06);
        var sectors = record.Length / bytesPerSector;

        if (usaCount != sectors + 1 ||
            usaOffset < 0x30 ||
            usaOffset + usaCount * 2 > record.Length)
        {
            return false;
        }

        var updateSequenceNumber = ReadUInt16(record, usaOffset);

        for (var sector = 0; sector < sectors; sector++)
        {
            var trailer = (sector + 1) * bytesPerSector - 2;

            if (ReadUInt16(record, trailer) != updateSequenceNumber)
            {
                return false;
            }
        }

        for (var sector = 0; sector < sectors; sector++)
        {
            var trailer = (sector + 1) * bytesPerSector - 2;
            var replacement = ReadUInt16(record, usaOffset + (sector + 1) * 2);
            BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(trailer, 2), replacement);
        }

        return true;
    }

    private static bool TryGetResidentValue(
        ReadOnlySpan<byte> record,
        int attributeStart,
        int attributeEnd,
        out int valueStart,
        out int valueLength)
    {
        valueStart = 0;
        valueLength = 0;

        if (attributeEnd - attributeStart < 0x18)
        {
            return false;
        }

        var length = ReadUInt32(record, attributeStart + 0x10);
        var offset = ReadUInt16(record, attributeStart + 0x14);

        if (length > int.MaxValue ||
            offset < 0x18 ||
            (long)attributeStart + offset + length > attributeEnd)
        {
            return false;
        }

        valueStart = attributeStart + offset;
        valueLength = (int)length;
        return true;
    }

    private static DateTime FileTime(ReadOnlySpan<byte> record, int offset)
    {
        try
        {
            var value = ReadInt64(record, offset);

            return value > 0
                ? DateTime.FromFileTimeUtc(value)
                : DateTime.MinValue;
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> record, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> record, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(offset, 4));

    private static ulong ReadUInt64(ReadOnlySpan<byte> record, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(offset, 8));

    private static long ReadInt64(ReadOnlySpan<byte> record, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(record.Slice(offset, 8));
}
