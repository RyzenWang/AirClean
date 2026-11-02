namespace AirClean.Core.Scanning.Mft;

/// <summary>One data run from an NTFS non-resident attribute.</summary>
internal readonly record struct MftDataRun(long StartVcn, long ClusterCount, long? StartLcn)
{
    public long EndVcn => StartVcn + ClusterCount;

    public bool Contains(long vcn) => vcn >= StartVcn && vcn < EndVcn;

    /// <summary>
    /// Parses the NTFS run list at <paramref name="offset"/>. A zero offset size means a
    /// sparse run; LCNs are stored relative to the previous run.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> record, int offset, int limit, out MftDataRun[] runs)
    {
        runs = [];
        var parsed = new List<MftDataRun>();

        long vcn = 0;
        long lcn = 0;
        var cursor = offset;

        while (cursor < limit)
        {
            var header = record[cursor++];

            if (header == 0)
            {
                runs = parsed.ToArray();
                return parsed.Count > 0;
            }

            var lengthSize = header & 0x0F;
            var offsetSize = (header >> 4) & 0x0F;

            if (lengthSize is 0 or > 8 || offsetSize > 8 ||
                cursor + lengthSize + offsetSize > limit)
            {
                return false;
            }

            var length = ReadUnsigned(record, ref cursor, lengthSize);

            if (length <= 0 || length > (1L << 40) || vcn > long.MaxValue - length)
            {
                return false;
            }

            long? startLcn = null;

            if (offsetSize > 0)
            {
                var delta = ReadSigned(record, ref cursor, offsetSize);

                if (delta > 0 && lcn > long.MaxValue - delta)
                {
                    return false;
                }

                if (delta < 0 && lcn < long.MinValue - delta)
                {
                    return false;
                }

                lcn += delta;

                if (lcn < 0)
                {
                    return false;
                }

                startLcn = lcn;
            }

            parsed.Add(new MftDataRun(vcn, length, startLcn));
            vcn += length;
        }

        return false;
    }

    private static long ReadUnsigned(ReadOnlySpan<byte> data, ref int offset, int size)
    {
        long value = 0;

        for (var index = 0; index < size; index++)
        {
            value |= (long)data[offset + index] << (index * 8);
        }

        offset += size;
        return value;
    }

    private static long ReadSigned(ReadOnlySpan<byte> data, ref int offset, int size)
    {
        var value = ReadUnsigned(data, ref offset, size);

        if (size >= 8)
        {
            return value;
        }

        var shift = 64 - size * 8;
        return (value << shift) >> shift;
    }
}
