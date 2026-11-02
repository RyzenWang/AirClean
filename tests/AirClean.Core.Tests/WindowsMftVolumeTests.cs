using AirClean.Core.Scanning.Mft;
using Xunit;

namespace AirClean.Core.Tests;

public sealed class WindowsMftVolumeTests
{
    [Fact]
    public void Reads_a_sector_aligned_block_from_a_file_handle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "airclean-mft-read", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            var data = new byte[8192];

            for (var index = 0; index < data.Length; index++)
            {
                data[index] = (byte)(index * 31);
            }

            File.WriteAllBytes(path, data);

            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[4096];

            WindowsMftVolume.ReadAligned(handle, 4096, buffer);

            Assert.Equal(data[4096..], buffer);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: false);
        }
    }
}
