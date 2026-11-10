using Xunit;

namespace AirClean.App.Tests;

public sealed class CrashLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "airclean-crash",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_the_source_and_the_exception()
    {
        var file = Path.Combine(_directory, "crash.log");

        CrashLog.WriteTo(file, "unit-test", new InvalidOperationException("boom"));

        var text = File.ReadAllText(file);
        Assert.Contains("unit-test", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void Rotates_the_previous_file_when_the_limit_is_exceeded()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "crash.log");
        File.WriteAllText(file, new string('x', (int)CrashLog.MaxBytes + 1));

        CrashLog.WriteTo(file, "second", null);

        Assert.True(File.Exists(Path.Combine(_directory, "crash.1.log")));
        Assert.True(new FileInfo(file).Length < CrashLog.MaxBytes);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
