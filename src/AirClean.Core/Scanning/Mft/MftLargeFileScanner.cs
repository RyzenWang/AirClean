using AirClean.Core.Models;
using AirClean.Core.Volumes;

namespace AirClean.Core.Scanning.Mft;

/// <summary>
/// Fast path for large-file scans on NTFS. It reads the MFT in a handful of sequential
/// reads, then resolves only the paths of files above the threshold. If the raw volume
/// cannot be opened (the normal case unless AirClean runs elevated), the caller falls back
/// to the directory walk.
/// </summary>
internal static class MftLargeFileScanner
{
    public static bool TryScan(
        VolumeInfo volume,
        long thresholdBytes,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken,
        out MftLargeFileResult? result)
    {
        result = null;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var mft = WindowsMftVolume.TryOpen(volume);

            if (mft is null)
            {
                return false;
            }

            var recordSize = mft.BytesPerFileRecord;
            var bufferSize = Math.Clamp(recordSize * 1024, 64 * 1024, 4 * 1024 * 1024);
            bufferSize -= bufferSize % recordSize;
            var buffer = new byte[bufferSize];
            var entries = new Dictionary<uint, MftEntry>();
            var total = mft.MftValidDataLength;
            var position = 0L;
            var filesSeen = 0L;
            var lastReport = DateTime.MinValue;

            while (position < total)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var count = (int)Math.Min(buffer.Length, total - position);
                count -= count % recordSize;

                if (count <= 0)
                {
                    break;
                }

                mft.ReadMft(position, buffer.AsSpan(0, count));

                for (var offset = 0; offset < count; offset += recordSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!MftRecordParser.TryParse(
                            buffer.AsSpan(offset, recordSize),
                            mft.BytesPerSector,
                            out var entry) ||
                        entry is null ||
                        !entry.InUse ||
                        entry.IsExtension)
                    {
                        continue;
                    }

                    if (!entry.IsDirectory)
                    {
                        filesSeen++;
                    }

                    var now = DateTime.UtcNow;

                    if ((now - lastReport).TotalMilliseconds >= 100)
                    {
                        lastReport = now;
                        progress?.Report(new ScanProgress(filesSeen, 0, 0, volume.Root));
                    }

                    // Small files still matter for the count, but not for path
                    // resolution or the top list. Keeping only directories and
                    // candidates keeps the index compact on multi-million-file disks.
                    if (!entry.IsDirectory && entry.Size < thresholdBytes)
                    {
                        continue;
                    }

                    entries[entry.RecordNumber] = entry;
                }

                position += count;
            }

            if (entries.Count == 0 ||
                filesSeen == 0 ||
                !entries.TryGetValue(5, out var root) ||
                !root.IsDirectory)
            {
                return false;
            }

            result = new MftIndex(entries).FindLargeFiles(volume, thresholdBytes, cancellationToken);

            progress?.Report(new ScanProgress(filesSeen, result.MatchedBytes, result.MatchedCount, volume.Root));
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A malformed or partially readable MFT must never break the scan; the caller
            // falls back to the directory walk.
            return false;
        }
    }
}
