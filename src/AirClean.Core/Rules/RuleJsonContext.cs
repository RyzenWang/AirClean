using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirClean.Core.Rules;

/// <summary>Source-generated rule-file serializer; required by trimming and NativeAOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(RuleFile))]
internal sealed partial class RuleJsonContext : JsonSerializerContext;
