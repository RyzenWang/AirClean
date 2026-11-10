using System.Globalization;
using System.Text.Json;
using AirClean.Core.Models;
using AirClean.Core.Services;

namespace AirClean.Core.Deleting;

/// <summary>
/// Reads and writes cleanup batches. Each batch is one JSON file under
/// <c>&lt;journal&gt;/&lt;yyyy-MM&gt;/&lt;utcTicks&gt;-&lt;batchId&gt;.json</c>, written
/// atomically (temp file + move) so a crash mid-write cannot leave a half record. The
/// monthly JSONL files written by journal v1 are still read, but they cannot be restored.
/// </summary>
public sealed class JournalStore
{
    public const int DefaultMaxBatches = 200;

    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(30);

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    private readonly string _root;
    private readonly int _maxBatches;
    private readonly TimeSpan _maxAge;
    private readonly object _gate = new();

    public JournalStore(string? directory = null, int maxBatches = DefaultMaxBatches, TimeSpan? maxAge = null)
    {
        if (maxBatches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatches), "At least one batch must be kept.");
        }

        _root = directory ?? Path.Combine(SettingsService.GetDefaultDirectory(), "journal");
        _maxBatches = maxBatches;
        _maxAge = maxAge ?? DefaultMaxAge;
    }

    public string DirectoryPath => _root;

    /// <summary>Writes one batch and prunes the journal. Never throws.</summary>
    internal JournalBatch Append(
        DeletionReport report,
        DeleteMode mode,
        IReadOnlyList<JournalEntry> entries,
        DateTimeOffset? at = null)
    {
        var batch = new JournalBatch(
            report.BatchId,
            at ?? DateTimeOffset.UtcNow,
            mode,
            report.Canceled,
            report.DeletedCount,
            report.TrashedBytes,
            report.FreedBytes,
            new Dictionary<SkipReason, int>(report.Skipped),
            entries.Select(entry => new JournalItem(entry.Path, entry.Size, entry.TrashPath, false)).ToArray());

        lock (_gate)
        {
            try
            {
                TryWrite(batch);
                PruneCore(batch.At);
            }
            catch
            {
                // The journal is a convenience; losing a record must never fail a cleanup.
            }
        }

        return batch;
    }

    /// <summary>Newest batches first, including v1 (legacy) history.</summary>
    public IReadOnlyList<JournalBatch> ReadRecent(int max = 5)
    {
        if (max <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            return LoadAll()
                .OrderByDescending(batch => batch.At)
                .Take(max)
                .ToArray();
        }
    }

    public JournalBatch? Find(Guid batchId)
    {
        lock (_gate)
        {
            return LoadAll().FirstOrDefault(batch => batch.BatchId == batchId);
        }
    }

    /// <summary>
    /// Marks the given original paths as restored. Returns false when the batch is missing
    /// (pruned) or is legacy history that cannot be rewritten.
    /// </summary>
    public bool MarkRestored(Guid batchId, IReadOnlyCollection<string> restoredPaths)
    {
        if (restoredPaths.Count == 0)
        {
            return false;
        }

        lock (_gate)
        {
            try
            {
                var file = FindBatchFile(batchId);
                if (file is null)
                {
                    return false;
                }

                var dto = ReadBatchFile(file);
                if (dto is null)
                {
                    return false;
                }

                var restored = new HashSet<string>(restoredPaths, PathComparer);
                foreach (var item in dto.Items)
                {
                    if (restored.Contains(item.P))
                    {
                        item.Restored = true;
                    }
                }

                return TryWriteFile(file, dto);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Records that the trash was emptied, so no batch can pretend to be restorable any
    /// more. With a volume root only batches whose items live on that volume are marked;
    /// without one (the Unix trash is global) every batch is marked. Returns how many
    /// batch files were updated.
    /// </summary>
    public int MarkTrashEmptied(string? volumeRoot = null)
    {
        lock (_gate)
        {
            var marked = 0;

            foreach (var file in EnumerateBatchFiles())
            {
                var dto = ReadBatchFile(file);
                if (dto is null || dto.TrashEmptied)
                {
                    continue;
                }

                if (volumeRoot is not null &&
                    !dto.Items.Any(item => IsUnderRoot(item.P, volumeRoot)))
                {
                    continue;
                }

                dto.TrashEmptied = true;
                if (TryWriteFile(file, dto))
                {
                    marked++;
                }
            }

            return marked;
        }
    }

    /// <summary>Deletes batches beyond the count/age limits. Never throws.</summary>
    public void Prune()
    {
        lock (_gate)
        {
            try
            {
                PruneCore(DateTimeOffset.UtcNow);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private void PruneCore(DateTimeOffset now)
    {
        var cutoff = now - _maxAge;
        var files = new List<(string File, long Ticks)>();

        foreach (var file in EnumerateBatchFiles())
        {
            if (TryReadTicks(file, out var ticks))
            {
                files.Add((file, ticks));
            }
        }

        files.Sort((left, right) => right.Ticks.CompareTo(left.Ticks));

        for (var index = 0; index < files.Count; index++)
        {
            var expired = new DateTimeOffset(files[index].Ticks, TimeSpan.Zero) < cutoff;
            if (expired || index >= _maxBatches)
            {
                TryDelete(files[index].File);
            }
        }

        CleanupEmptyMonthDirectories();
    }

    private IEnumerable<JournalBatch> LoadAll()
    {
        foreach (var file in EnumerateBatchFiles())
        {
            var dto = ReadBatchFile(file);
            if (dto is { BatchId: var id } && id != Guid.Empty)
            {
                yield return ToBatch(dto, legacy: false);
            }
        }

        foreach (var file in EnumerateLegacyFiles())
        {
            foreach (var line in ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JournalBatchDto? dto = null;
                try
                {
                    dto = JsonSerializer.Deserialize(line, JournalJsonContext.Default.JournalBatchDto);
                }
                catch
                {
                    // A corrupt line must not hide the rest of the history.
                }

                if (dto is not null && dto.BatchId != Guid.Empty)
                {
                    yield return ToBatch(dto, legacy: true);
                }
            }
        }
    }

    private bool TryWrite(JournalBatch batch)
    {
        var stamp = batch.At.ToUniversalTime();
        var directory = Path.Combine(_root, stamp.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        var file = Path.Combine(directory, $"{stamp.UtcTicks:D19}-{batch.BatchId:N}.json");
        return TryWriteFile(file, ToDto(batch));
    }

    private static bool TryWriteFile(string file, JournalBatchDto dto)
    {
        var temporary = file + ".tmp";

        try
        {
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(dto, JournalJsonContext.Default.JournalBatchDto);
            File.WriteAllText(temporary, json);
            File.Move(temporary, file, overwrite: true);
            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch
            {
                // Nothing else to do.
            }

            return false;
        }
    }

    private JournalBatchDto? ReadBatchFile(string file)
    {
        try
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize(json, JournalJsonContext.Default.JournalBatchDto);
        }
        catch
        {
            return null;
        }
    }

    private string? FindBatchFile(Guid batchId)
    {
        var suffix = "-" + batchId.ToString("N") + ".json";
        foreach (var file in EnumerateBatchFiles())
        {
            if (Path.GetFileName(file).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateBatchFiles()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private IEnumerable<string> EnumerateLegacyFiles()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> ReadLines(string file)
    {
        try
        {
            return File.ReadAllLines(file);
        }
        catch
        {
            return [];
        }
    }

    private static bool TryReadTicks(string file, out long ticks)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var dash = name.IndexOf('-', StringComparison.Ordinal);
        var prefix = dash > 0 ? name[..dash] : name;
        return long.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out ticks);
    }

    private void CleanupEmptyMonthDirectories()
    {
        try
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }
        catch
        {
            // Leaving an empty folder behind is harmless.
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch
        {
            // Locked files go on the next prune.
        }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        if (root is "/" or "\\")
        {
            return true;
        }

        var normalized = root.TrimEnd('\\', '/');
        if (normalized.Length == 0)
        {
            return false;
        }

        return path.StartsWith(normalized, PathComparison) &&
               (path.Length == normalized.Length || path[normalized.Length] is '\\' or '/');
    }

    private static JournalBatchDto ToDto(JournalBatch batch) => new()
    {
        V = 2,
        BatchId = batch.BatchId,
        At = batch.At,
        Mode = batch.Mode.ToString().ToLowerInvariant(),
        Canceled = batch.Canceled,
        TrashEmptied = batch.TrashEmptied,
        Deleted = batch.DeletedCount,
        TrashedBytes = batch.TrashedBytes,
        FreedBytes = batch.FreedBytes,
        Skipped = new Dictionary<SkipReason, int>(batch.Skipped),
        Items = batch.Items
            .Select(item => new JournalItemDto
            {
                P = item.Path,
                S = item.Size,
                R = item.TrashPath,
                Restored = item.Restored
            })
            .ToList()
    };

    private static JournalBatch ToBatch(JournalBatchDto dto, bool legacy)
    {
        var mode = Enum.TryParse<DeleteMode>(dto.Mode, ignoreCase: true, out var parsed)
            ? parsed
            : DeleteMode.Trash;

        var items = (dto.Items ?? [])
            .Select(item => new JournalItem(item.P, item.S, item.R, item.Restored))
            .ToArray();

        return new JournalBatch(
            dto.BatchId,
            dto.At,
            mode,
            dto.Canceled,
            dto.Deleted,
            dto.TrashedBytes,
            dto.FreedBytes,
            dto.Skipped is null
                ? new Dictionary<SkipReason, int>()
                : new Dictionary<SkipReason, int>(dto.Skipped),
            items,
            legacy,
            dto.TrashEmptied);
    }
}
