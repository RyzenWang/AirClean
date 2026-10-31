using AirClean.App.Services;
using AirClean.Core.Formatting;
using AirClean.Core.Volumes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirClean.App.ViewModels;

/// <summary>One drive in the volume picker.</summary>
public sealed partial class VolumeItemViewModel : ObservableObject
{
    private bool _isSelected;

    public VolumeItemViewModel(VolumeInfo info, LocalizationService localization, Action<VolumeItemViewModel> choose)
    {
        Info = info;

        var name = string.IsNullOrWhiteSpace(info.Label)
            ? localization.T(info.IsSystem ? "L.SystemVolume" : "L.LocalVolume")
            : info.Label;

        var root = info.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Name = string.IsNullOrEmpty(root) ? name : $"{name} ({root})";
        DisplayName = localization.Format("L.VolumeItemFormat", Name, ByteSize.Format(info.FreeBytes));
        FreeText = localization.Format("L.VolumeFreeFormat", ByteSize.Format(info.FreeBytes));
        ChooseCommand = new RelayCommand(() => choose(this));
    }

    public VolumeInfo Info { get; }

    public string Name { get; }

    public string DisplayName { get; private set; }

    public string FreeText { get; }

    public string Root => Info.Root;

    public bool IsSystem => Info.IsSystem;

    public long FreeBytes => Info.FreeBytes;

    public double UsedRatio => Info.UsedRatio;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public IRelayCommand ChooseCommand { get; }

    /// <summary>Re-reads the localized strings after a language switch.</summary>
    public void RefreshLocalization(LocalizationService localization)
    {
        var name = string.IsNullOrWhiteSpace(Info.Label)
            ? localization.T(Info.IsSystem ? "L.SystemVolume" : "L.LocalVolume")
            : Info.Label;

        var root = Info.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DisplayName = localization.Format(
            "L.VolumeItemFormat",
            string.IsNullOrEmpty(root) ? name : $"{name} ({root})",
            ByteSize.Format(Info.FreeBytes));
        OnPropertyChanged(nameof(DisplayName));
    }
}
