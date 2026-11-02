using System.Text.Json.Serialization;
using AirClean.Core.Models;

namespace AirClean.Core.Services;

/// <summary>Source-generated settings serializer; required by trimming and NativeAOT.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
