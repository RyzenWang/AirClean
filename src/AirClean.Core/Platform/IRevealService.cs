namespace AirClean.Core.Platform;

/// <summary>Opens the system file manager with one file selected.</summary>
public interface IRevealService
{
    bool IsSupported { get; }

    void Reveal(string path);
}
