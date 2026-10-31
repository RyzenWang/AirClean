using System.Text.Json;
using AirClean.Core.Models;

namespace AirClean.Core.Rules;

public sealed class ScanRule
{
    public required string Id { get; init; }

    public required CleanCategory Category { get; init; }

    public required Safety Safety { get; init; }

    public required string[] Roots { get; init; }

    public string[] Include { get; init; } = ["*"];

    public string[] Exclude { get; init; } = [];

    public int MaxDepth { get; init; } = 6;

    public int MinAgeDays { get; init; } = 1;

    public bool EmptyTrash { get; init; }

    public bool RequiresElevation { get; init; }

    public string Source { get; init; } = string.Empty;
}

public sealed class RuleSet
{
    private RuleSet(IReadOnlyList<ScanRule> rules, string platform)
    {
        Rules = rules;
        Platform = platform;
    }

    public IReadOnlyList<ScanRule> Rules { get; }

    public string Platform { get; }

    public static string CurrentPlatform =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" : "linux";

    public static RuleSet ForCurrentPlatform() => LoadEmbedded(CurrentPlatform);

    public static RuleSet LoadEmbedded(string platform)
    {
        var assembly = typeof(RuleSet).Assembly;
        var name = $"AirClean.Core.Rules.rules.{platform}.json";

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded rule file '{name}'.");

        return Load(stream);
    }

    public static RuleSet Load(Stream stream)
    {
        var file = JsonSerializer.Deserialize<RuleFile>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidDataException("Rule file is empty.");

        if (file.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported rule schema version {file.SchemaVersion}.");
        }

        var rules = new List<ScanRule>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in file.Rules)
        {
            var rule = Map(dto);

            if (!ids.Add(rule.Id))
            {
                throw new InvalidDataException($"Duplicate rule id '{rule.Id}'.");
            }

            if (!rule.EmptyTrash && rule.Roots.Length == 0)
            {
                throw new InvalidDataException($"Rule '{rule.Id}' has no roots.");
            }

            rules.Add(rule);
        }

        return new RuleSet(rules, file.Platform);
    }

    private static ScanRule Map(RuleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new InvalidDataException("A rule is missing its id.");
        }

        if (!Enum.TryParse<CleanCategory>(dto.Category, ignoreCase: true, out var category))
        {
            throw new InvalidDataException($"Rule '{dto.Id}' has unknown category '{dto.Category}'.");
        }

        if (!Enum.TryParse<Safety>(dto.Safety, ignoreCase: true, out var safety))
        {
            throw new InvalidDataException($"Rule '{dto.Id}' has unknown safety '{dto.Safety}'.");
        }

        var emptyTrash = string.Equals(dto.Action, "empty-trash", StringComparison.OrdinalIgnoreCase);
        if (!emptyTrash && !string.Equals(dto.Action, "contents", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Rule '{dto.Id}' has unknown action '{dto.Action}'.");
        }

        if (dto.MaxDepth is < 1 or > 12)
        {
            throw new InvalidDataException($"Rule '{dto.Id}' has maxDepth outside 1..12.");
        }

        if (dto.MinAgeDays is < 0 or > 3650)
        {
            throw new InvalidDataException($"Rule '{dto.Id}' has minAgeDays outside 0..3650.");
        }

        return new ScanRule
        {
            Id = dto.Id,
            Category = category,
            Safety = safety,
            Roots = dto.Roots ?? [],
            Include = dto.Include is { Length: > 0 } ? dto.Include : ["*"],
            Exclude = dto.Exclude ?? [],
            MaxDepth = dto.MaxDepth,
            MinAgeDays = dto.MinAgeDays,
            EmptyTrash = emptyTrash,
            RequiresElevation = dto.RequiresElevation,
            Source = dto.Source ?? string.Empty
        };
    }

    private sealed class RuleFile
    {
        public int SchemaVersion { get; set; } = 1;

        public string Platform { get; set; } = string.Empty;

        public List<RuleDto> Rules { get; set; } = [];
    }

    private sealed class RuleDto
    {
        public string Id { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Safety { get; set; } = string.Empty;

        public string Action { get; set; } = "contents";

        public string[]? Roots { get; set; }

        public string[]? Include { get; set; }

        public string[]? Exclude { get; set; }

        public int MaxDepth { get; set; } = 6;

        public int MinAgeDays { get; set; } = 1;

        public bool RequiresElevation { get; set; }

        public string? Source { get; set; }
    }
}
