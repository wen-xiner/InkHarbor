using System.Windows.Media;
using InkHarbor.Core.Models;
using InkHarbor.Core.Services;

namespace InkHarbor.App.ViewModels;

public sealed class CorpusItemViewModel : ObservableObject
{
    private readonly Action<CorpusItemViewModel> _onToggled;
    private bool _isSelected;

    public CorpusItemViewModel(CorpusFile file, Action<CorpusItemViewModel> onToggled)
    {
        File = file;
        _onToggled = onToggled;
    }

    public CorpusFile File { get; }

    public string DisplayName => File.DisplayName;

    public string TagText => File.TagText;

    public string MetaText => $"{File.WordCount} 字 · 约 {File.EstimatedTokens} token";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onToggled(this);
            }
        }
    }
}
