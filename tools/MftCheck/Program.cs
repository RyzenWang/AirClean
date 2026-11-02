using System.Diagnostics;
using System.Security.Principal;
using AirClean.Core.Scanning;
using AirClean.Core.Scanning.Mft;
using AirClean.Core.Volumes;
using AirClean.Platform.Admin;

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

var viaHelperIndex = Array.IndexOf(args, "--via-helper");
var helperPath = viaHelperIndex >= 0 && viaHelperIndex + 1 < args.Length
    ? args[viaHelperIndex + 1]
    : null;

if (args.Contains("--admin-scan") || args.Contains("--admin-clean"))
{
    if (helperPath is null)
    {
        Console.Error.WriteLine("--admin-scan/--admin-clean require --via-helper <AirClean.exe>.");
        return 2;
    }

    var client = new AdminHelperClient(helperPath) { Enabled = true };
    var adminWatch = Stopwatch.StartNew();

    if (args.Contains("--admin-clean"))
    {
        var cleanResult = client.Clean(AdminRuleIds.WindowsUpdateCache, progress: null, CancellationToken.None);
        adminWatch.Stop();

        if (cleanResult is null)
        {
            Console.Error.WriteLine("The elevated helper admin cleanup failed.");
            return 1;
        }

        Console.WriteLine(
            $"Admin clean: {cleanResult.FileCount:N0} files, {cleanResult.Bytes:N0} bytes, " +
            $"{cleanResult.Skipped:N0} skipped, {adminWatch.Elapsed.TotalSeconds:F2}s");
        return 0;
    }

    var adminResult = client.Scan(AdminRuleIds.WindowsUpdateCache, CancellationToken.None);
    adminWatch.Stop();

    if (adminResult is null)
    {
        Console.Error.WriteLine("The elevated helper admin scan failed.");
        return 1;
    }

    Console.WriteLine(
        $"Admin scan: {adminResult.RuleId}, {adminResult.FileCount:N0} files, " +
        $"{adminResult.Bytes:N0} bytes, {adminWatch.Elapsed.TotalSeconds:F2}s");
    return 0;
}

if (helperPath is not null)
{
    var client = new AdminHelperClient(helperPath) { Enabled = true };
    var helperWatch = Stopwatch.StartNew();
    var helperResult = client.TryScan(volume, 1024 * 1024, progress: null, CancellationToken.None);
    helperWatch.Stop();

    if (helperResult is null)
    {
        Console.Error.WriteLine("The elevated helper round-trip failed.");
        return 1;
    }

    Console.WriteLine(
        $"Helper: {helperResult.MatchedCount:N0} files >= 1 MB, " +
        $"{helperResult.MatchedBytes:N0} bytes total, top {helperResult.Items.Count}, " +
        $"{helperWatch.Elapsed.TotalSeconds:F2}s");

    foreach (var item in helperResult.Items.Take(5))
    {
        Console.WriteLine($"  {item.Size,15:N0}  {item.Path}");
    }

    return helperResult.MatchedCount > 0 ? 0 : 1;
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
