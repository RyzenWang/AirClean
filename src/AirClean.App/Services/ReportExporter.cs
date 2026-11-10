using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace AirClean.App.Services;

/// <summary>Saves a generated report to a user-chosen file.</summary>
public interface IReportExporter
{
    Task<bool> SaveAsync(string suggestedFileName, string content);
}

/// <summary>
/// Asks for a path through the window's storage provider. Returns false when the user
/// cancels or the platform cannot open a picker; callers only need to say what happened.
/// </summary>
public sealed class FileReportExporter : IReportExporter
{
    public async Task<bool> SaveAsync(string suggestedFileName, string content)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not { } window)
            {
                return false;
            }

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "csv",
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV") { Patterns = ["*.csv"] }
                ]
            });

            if (file is null)
            {
                return false;
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await writer.WriteAsync(content);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
