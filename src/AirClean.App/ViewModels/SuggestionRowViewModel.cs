using AirClean.App.Services;
using AirClean.Core.Formatting;
using AirClean.Core.Suggestions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirClean.App.ViewModels;

/// <summary>
/// One read-only suggestion. This is display-only by design: there is no selection
/// state, and nothing here feeds <c>Categories</c>, the deletion plan or the cleanup
/// confirmation.
/// </summary>
public sealed class SuggestionRowViewModel : ObservableObject
{
    private readonly ReadOnlySuggestion _suggestion;
    private readonly LocalizationService _localization;
    private readonly Action<string>? _copyText;

    public SuggestionRowViewModel(
        ReadOnlySuggestion suggestion,
        LocalizationService localization,
        Action<string>? copyText = null)
    {
        _suggestion = suggestion;
        _localization = localization;
        _copyText = copyText;
        CopyCommand = new RelayCommand(Copy, () => HasCommand);
    }

    public string Label => _localization.T(_suggestion.Label);

    public string SizeText => _suggestion.Bytes is { } bytes
        ? ByteSize.Format(bytes)
        : _localization.T("L.SuggestionSizeUnknown");

    public string Fact => _localization.T(_suggestion.FactKey);

    public string Command => _suggestion.SuggestedCommand ?? string.Empty;

    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);

    public IRelayCommand CopyCommand { get; }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(Fact));
    }

    private void Copy()
    {
        if (HasCommand)
        {
            _copyText?.Invoke(Command);
        }
    }
}
