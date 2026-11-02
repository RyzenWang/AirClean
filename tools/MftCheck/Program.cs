using System.Diagnostics;
using System.Security.Principal;
using AirClean.Core.Scanning.Mft;
using AirClean.Core.Volumes;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("MftCheck only runs on Windows.");
    return 2;
}

using var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);

if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.Error.WriteLine("MftCheck must run elevated to open the raw NTFS volume.");
    return 2;
}

var volume = VolumeService.List().FirstOrDefault(candidate => candidate.IsSystem);

if (volume is null)
{
    Console.Error.WriteLine("No system volume was found.");
    return 2;
}

Console.WriteLine($"Volume: {volume.Root} ({volume.FileSystem})");

using (var mft = WindowsMftVolume.TryOpen(volume))
{
    if (mft is null)
    {
        Console.Error.WriteLine("Could not open the MFT volume.");
        return 1;
    }

    Console.WriteLine(
        $"MFT: {mft.MftValidDataLength:N0} bytes, " +
        $"{mft.BytesPerFileRecord} bytes/record, {mft.BytesPerSector} bytes/sector, " +
        $"{mft.MftValidDataLength / mft.BytesPerFileRecord:N0} records");
}

var stopwatch = Stopwatch.StartNew();
var usedMft = MftLargeFileScanner.TryScan(
    volume,
    thresholdBytes: 1024 * 1024,
    progress: null,
    CancellationToken.None,
    out var result,
    out var stats);
stopwatch.Stop();

if (!usedMft || result is null)
{
    Console.Error.WriteLine("MFT fast path was not used; the scanner would fall back to the directory walk.");
    return 1;
}

Console.WriteLine(
    $"MFT: {result.MatchedCount:N0} files >= 1 MB, " +
    $"{result.MatchedBytes:N0} bytes total, top {result.Items.Count}, " +
    $"{stopwatch.Elapsed.TotalSeconds:F2}s");

if (stats is not null)
{
    Console.WriteLine(
        $"  records={stats.RecordsRead:N0}, read={stats.ReadMilliseconds:N0}ms, " +
        $"parse={stats.ParseMilliseconds:N0}ms, index={stats.IndexMilliseconds:N0}ms");
}

foreach (var item in result.Items.Take(5))
{
    Console.WriteLine($"  {item.Size,15:N0}  {item.Path}");
}

return result.MatchedCount > 0 ? 0 : 1;
