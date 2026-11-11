namespace AirClean.Core.Models;

/// <summary>
/// Everything the app persists between runs. Kept deliberately small; scan rules and
/// journals live elsewhere. <see cref="SchemaVersion"/> is bumped when the shape changes;
/// <see cref="Services.SettingsService"/> normalises older files instead of rejecting them.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>One of <c>System</c>, <c>Light</c>, <c>Dark</c>.</summary>
    public string Theme { get; set; } = "System";

    /// <summary><c>en</c> or <c>zh-CN</c>.</summary>
    public string Language { get; set; } = "en";

    public double WindowWidth { get; set; } = 1240;

    public double WindowHeight { get; set; } = 860;

    public bool WindowMaximized { get; set; }

    /// <summary>Ask the elevated helper for the MFT fast path on each NTFS scan.</summary>
    public bool UseAdminHelper { get; set; }

    /// <summary><c>Quick</c> scans known caches, logs and the trash; <c>Full</c> also
    /// walks the whole volume for large files and duplicates.</summary>
    public string ScanMode { get; set; } = "Quick";

    /// <summary>Paths the user never wants scanned or deleted.</summary>
    public List<string> Exclusions { get; set; } = [];

    public int LargeFileThresholdMb { get; set; } = 100;

    public int LargeFileLimit { get; set; } = 200;

    public int DuplicateMinSizeMb { get; set; } = 1;

    public int DuplicateGroupLimit { get; set; } = 200;

    public int HashBudgetGb { get; set; } = 8;

    public bool CheckForUpdatesOnStartup { get; set; } = true;

    public bool CloseToTray { get; set; }

    public bool MinimizeToTray { get; set; }

    public bool ReminderEnabled { get; set; }

    public int ReminderIntervalDays { get; set; } = 7;

    public DateTimeOffset? LastReminderAt { get; set; }

    public bool RunAtLogin { get; set; }
}
