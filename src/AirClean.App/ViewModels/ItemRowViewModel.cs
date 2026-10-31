using AirClean.Core.Formatting;
using AirClean.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirClean.App.ViewModels;

/// <summary>One row inside an expanded category preview.</summary>
public sealed partial class ItemRowViewModel : ObservableObject
{
    private readonly Action? _onSelectionChanged;
    private readonly Action<string>? _reveal;
    private bool _isSelected;

    public ItemRowViewModel(
        CleanItem item,
        bool selected,
        bool revealSupported,
        Action? onSelectionChanged,
        Action<string>? reveal)
    {
        Item = item;
        IsRevealSupported = revealSupported;
        _isSelected = selected;
        _onSelectionChanged = onSelectionChanged;
        _reveal = reveal;

        FullPath = item.Path;
        Name = Path.GetFileName(item.Path);
        Directory = Path.GetDirectoryName(item.Path) ?? string.Empty;
        SizeText = ByteSize.Format(item.Size);
    }

    public CleanItem Item { get; }

    public string Name { get; }

    public string Directory { get; }

    public string FullPath { get; }

    public string SizeText { get; }

    public bool IsRevealSupported { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onSelectionChanged?.Invoke();
            }
        }
    }

    [RelayCommand]
    private void Reveal() => _reveal?.Invoke(FullPath);
}
