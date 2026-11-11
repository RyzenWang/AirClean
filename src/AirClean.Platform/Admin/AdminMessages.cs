using System.Text.Json.Serialization;

namespace AirClean.Platform.Admin;

/// <summary>Source-generated helper protocol serializer; required by trimming and NativeAOT.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AdminRequest))]
[JsonSerializable(typeof(AdminEvent))]
internal sealed partial class AdminJsonContext : JsonSerializerContext;

internal sealed class AdminRequest
{
    public string Token { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string VolumeRoot { get; set; } = string.Empty;

    public long ThresholdBytes { get; set; }

    public int MaxItems { get; set; }

    public string? RuleId { get; set; }
}

internal sealed class AdminEvent
{
    public string Kind { get; set; } = string.Empty;

    public string? RuleId { get; set; }

    public long FilesSeen { get; set; }

    public long MatchedBytes { get; set; }

    public int MatchedCount { get; set; }

    public int Skipped { get; set; }

    public string? Message { get; set; }

    public List<AdminFile>? Items { get; set; }
}

internal sealed class AdminFile
{
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTime LastWriteUtc { get; set; }
}
