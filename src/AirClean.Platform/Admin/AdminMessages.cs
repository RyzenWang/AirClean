using System.Text.Json;

namespace AirClean.Platform.Admin;

internal static class AdminJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

internal sealed class AdminRequest
{
    public string Token { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string VolumeRoot { get; set; } = string.Empty;

    public long ThresholdBytes { get; set; }
}

internal sealed class AdminEvent
{
    public string Kind { get; set; } = string.Empty;

    public long FilesSeen { get; set; }

    public long MatchedBytes { get; set; }

    public int MatchedCount { get; set; }

    public string? Message { get; set; }

    public List<AdminFile>? Items { get; set; }
}

internal sealed class AdminFile
{
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTime LastWriteUtc { get; set; }
}
