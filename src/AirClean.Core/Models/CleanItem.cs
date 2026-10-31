namespace AirClean.Core.Models;

/// <summary>One file that a clean rule matched. Directories are deleted implicitly once empty.</summary>
public sealed record CleanItem(string Path, string Root, long Size, DateTime LastWriteUtc);
