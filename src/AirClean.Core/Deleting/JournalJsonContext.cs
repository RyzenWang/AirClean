using System.Text.Json.Serialization;
using AirClean.Core.Models;

namespace AirClean.Core.Deleting;

/// <summary>Source-generated journal serializer; required by trimming and NativeAOT.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JournalBatchDto))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;

/// <summary>
/// On-disk shape of one batch. Version 1 (monthly JSONL) used the same field names without
/// <see cref="Canceled"/>, <see cref="JournalItemDto.Restored"/> or
/// <see cref="JournalItemDto.R"/>; those simply deserialize to their defaults.
/// </summary>
internal sealed class JournalBatchDto
{
    public int V { get; set; } = 2;

    public Guid BatchId { get; set; }

    public DateTimeOffset At { get; set; }

    public string Mode { get; set; } = "trash";

    public bool Canceled { get; set; }

    public bool TrashEmptied { get; set; }

    public long Deleted { get; set; }

    public long TrashedBytes { get; set; }

    public long FreedBytes { get; set; }

    public Dictionary<SkipReason, int> Skipped { get; set; } = [];

    public List<JournalItemDto> Items { get; set; } = [];
}

internal sealed class JournalItemDto
{
    public string P { get; set; } = string.Empty;

    public long S { get; set; }

    /// <summary>Location inside the trash, when the platform reports one.</summary>
    public string? R { get; set; }

    public bool Restored { get; set; }
}
