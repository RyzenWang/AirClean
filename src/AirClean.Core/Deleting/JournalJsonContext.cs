using System.Text.Json.Serialization;
using AirClean.Core.Models;

namespace AirClean.Core.Deleting;

/// <summary>Source-generated journal serializer; required by trimming and NativeAOT.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JournalPayload))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;

internal sealed class JournalPayload
{
    public int V { get; set; } = 1;

    public Guid BatchId { get; set; }

    public DateTimeOffset At { get; set; }

    public string Mode { get; set; } = string.Empty;

    public long Deleted { get; set; }

    public long TrashedBytes { get; set; }

    public long FreedBytes { get; set; }

    public Dictionary<SkipReason, int> Skipped { get; set; } = [];

    public List<JournalEntry> Items { get; set; } = [];
}
