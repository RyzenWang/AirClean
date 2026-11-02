using System.Buffers;
using System.Runtime.InteropServices;
using AirClean.Core.Volumes;
using Microsoft.Win32.SafeHandles;

namespace AirClean.Core.Scanning.Mft;

/// <summary>
/// Reads the NTFS Master File Table through the raw volume handle. This is the only path
/// that can enumerate a large volume in seconds; it needs read access to <c>\\.\C:</c>,
/// which Windows grants only to elevated processes. Callers must treat a null return as
/// "fall back to the directory walk".
/// </summary>
internal sealed class WindowsMftVolume : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FsctlGetNtfsVolumeData = 0x00090064;

    private readonly SafeFileHandle _handle;
    private readonly MftDataRun[] _runs;
    private readonly int _bytesPerCluster;
    private readonly int _bytesPerSector;

    private WindowsMftVolume(
        SafeFileHandle handle,
        MftDataRun[] runs,
        int bytesPerSector,
        int bytesPerCluster,
        int bytesPerFileRecord,
        long mftValidDataLength)
    {
        _handle = handle;
        _runs = runs;
        _bytesPerSector = bytesPerSector;
        _bytesPerCluster = bytesPerCluster;
        BytesPerFileRecord = bytesPerFileRecord;
        MftValidDataLength = mftValidDataLength;
    }

    public int BytesPerFileRecord { get; }

    public int BytesPerSector => _bytesPerSector;

    public long MftValidDataLength { get; }

    public static WindowsMftVolume? TryOpen(VolumeInfo volume)
    {
        if (!OperatingSystem.IsWindows() ||
            !string.Equals(volume.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var root = volume.Root.TrimEnd('\\');
        if (root.Length != 2 || !char.IsLetter(root[0]) || root[1] != ':')
        {
            return null;
        }

        var handle = CreateFile(
            @"\\.\" + root,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        var transferred = false;

        try
        {
            var data = QueryVolumeData(handle);

            if (data is null)
            {
                return null;
            }

            var value = data.Value;

            if (!IsValidGeometry(value))
            {
                return null;
            }

            var record0 = new byte[value.BytesPerFileRecordSegment];
            ReadVolume(
                handle,
                (int)value.BytesPerSector,
                value.MftStartLcn * (long)value.BytesPerCluster,
                record0);

            if (!MftRecordParser.TryParse(record0, (int)value.BytesPerSector, out var mft) ||
                mft?.DataRuns is not { Length: > 0 } runs)
            {
                return null;
            }

            var coveredClusters = runs.Sum(run => run.ClusterCount);
            var coveredBytes = coveredClusters * (long)value.BytesPerCluster;

            if (coveredBytes < value.MftValidDataLength)
            {
                return null;
            }

            transferred = true;

            return new WindowsMftVolume(
                handle,
                runs,
                (int)value.BytesPerSector,
                (int)value.BytesPerCluster,
                (int)value.BytesPerFileRecordSegment,
                value.MftValidDataLength);
        }
        catch (Exception)
        {
            // Raw volume data is untrusted: any failure means "use the normal walk".
            return null;
        }
        finally
        {
            if (!transferred)
            {
                handle.Dispose();
            }
        }
    }

    public void ReadMft(long offset, Span<byte> destination)
    {
        var written = 0;

        while (written < destination.Length)
        {
            var streamOffset = offset + written;
            var vcn = streamOffset / _bytesPerCluster;
            var index = FindRun(vcn);

            if (index < 0)
            {
                index = ~index;

                if (index >= _runs.Length)
                {
                    destination[written..].Clear();
                    return;
                }

                var nextStart = _runs[index].StartVcn * (long)_bytesPerCluster;
                var gap = nextStart - streamOffset;

                if (gap <= 0)
                {
                    return;
                }

                var gapChunk = (int)Math.Min(gap, destination.Length - written);
                destination.Slice(written, gapChunk).Clear();
                written += gapChunk;
                continue;
            }

            var run = _runs[index];
            var runStart = run.StartVcn * (long)_bytesPerCluster;
            var withinRun = streamOffset - runStart;
            var available = run.ClusterCount * (long)_bytesPerCluster - withinRun;
            var chunk = (int)Math.Min(available, destination.Length - written);

            if (run.StartLcn is null)
            {
                destination.Slice(written, chunk).Clear();
            }
            else
            {
                ReadVolume(
                    _handle,
                    _bytesPerSector,
                    run.StartLcn.Value * (long)_bytesPerCluster + withinRun,
                    destination.Slice(written, chunk));
            }

            written += chunk;
        }
    }

    public void Dispose() => _handle.Dispose();

    private int FindRun(long vcn)
    {
        var low = 0;
        var high = _runs.Length - 1;

        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var run = _runs[middle];

            if (vcn < run.StartVcn)
            {
                high = middle - 1;
            }
            else if (vcn >= run.EndVcn)
            {
                low = middle + 1;
            }
            else
            {
                return middle;
            }
        }

        return ~low;
    }

    private static NtfsVolumeData? QueryVolumeData(SafeFileHandle handle)
    {
        var size = Marshal.SizeOf<NtfsVolumeData>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            if (!DeviceIoControl(
                    handle,
                    FsctlGetNtfsVolumeData,
                    IntPtr.Zero,
                    0,
                    buffer,
                    (uint)size,
                    out var returned,
                    IntPtr.Zero) ||
                returned < (uint)size)
            {
                return null;
            }

            return Marshal.PtrToStructure<NtfsVolumeData>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsValidGeometry(NtfsVolumeData data)
    {
        if (data.BytesPerSector is < 512 or > 4096 ||
            (data.BytesPerSector & (data.BytesPerSector - 1)) != 0 ||
            data.BytesPerCluster < data.BytesPerSector ||
            (data.BytesPerCluster & (data.BytesPerCluster - 1)) != 0 ||
            data.BytesPerFileRecordSegment is < 512 or > 65_536 ||
            data.BytesPerFileRecordSegment % data.BytesPerSector != 0 ||
            data.MftValidDataLength <= 0 ||
            data.MftStartLcn < 0)
        {
            return false;
        }

        return true;
    }

    private static void ReadVolume(SafeFileHandle handle, int bytesPerSector, long offset, Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            return;
        }

        var alignedOffset = offset - offset % bytesPerSector;
        var lead = (int)(offset - alignedOffset);
        var total = lead + destination.Length;
        var padded = (total + bytesPerSector - 1) / bytesPerSector * bytesPerSector;

        if (lead == 0 && padded == total)
        {
            ReadAligned(handle, alignedOffset, destination);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(padded);

        try
        {
            ReadAligned(handle, alignedOffset, rented.AsSpan(0, padded));
            rented.AsSpan(lead, destination.Length).CopyTo(destination);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    internal static unsafe void ReadAligned(SafeFileHandle handle, long offset, Span<byte> destination)
    {
        if (!SetFilePointerEx(handle, offset, out _, 0))
        {
            throw new IOException("Could not seek the NTFS volume.");
        }

        // Raw volume I/O is commonly unbuffered, so the buffer itself must be sector
        // aligned, while the caller's Span may not be. Read into an aligned scratch block
        // and copy the requested bytes back.
        var scratch = Marshal.AllocHGlobal(destination.Length + 4095);

        try
        {
            var aligned = (byte*)(((long)scratch + 4095) & ~4095L);
            var current = aligned;
            var remaining = destination.Length;

            while (remaining > 0)
            {
                var request = (uint)Math.Min(remaining, 1024 * 1024);

                if (!ReadFile(handle, (IntPtr)current, request, out var read, IntPtr.Zero))
                {
                    throw new IOException("Could not read the NTFS volume.");
                }

                if (read == 0)
                {
                    throw new IOException("Unexpected end of the NTFS volume.");
                }

                current += read;
                remaining -= (int)read;
            }

            fixed (byte* target = destination)
            {
                Buffer.MemoryCopy(aligned, target, destination.Length, destination.Length);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(scratch);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle file,
        long distanceToMove,
        out long newFilePointer,
        uint moveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle file,
        IntPtr buffer,
        uint numberOfBytesToRead,
        out uint numberOfBytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct NtfsVolumeData
    {
        public long VolumeSerialNumber;
        public long NumberSectors;
        public long TotalClusters;
        public long FreeClusters;
        public long TotalReserved;
        public uint BytesPerSector;
        public uint BytesPerCluster;
        public uint BytesPerFileRecordSegment;
        public uint ClustersPerFileRecordSegment;
        public long MftValidDataLength;
        public long MftStartLcn;
        public long Mft2StartLcn;
        public long MftZoneStart;
        public long MftZoneEnd;
    }
}
