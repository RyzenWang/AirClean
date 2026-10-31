namespace AirClean.Core.Models;

/// <summary>
/// Everything the app persists between runs. Kept deliberately small; scan rules and
/// journals live elsewhere.
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>One of <c>System</c>, <c>Light</c>, <c>Dark</c>.</summary>
    public string Theme { get; set; } = "System";

    /// <summary><c>en</c> or <c>zh-CN</c>.</summary>
    public string Language { get; set; } = "en";

    public double WindowWidth { get; set; } = 1240;

    public double WindowHeight { get; set; } = 860;

    public bool WindowMaximized { get; set; }
}
