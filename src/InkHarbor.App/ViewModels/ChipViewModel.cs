namespace InkHarbor.App.ViewModels;

/// <summary>配方条上的一颗胶囊。</summary>
public sealed class ChipViewModel
{
    public ChipViewModel(string fileName, string displayName)
    {
        FileName = fileName;
        DisplayName = displayName;
    }

    public string FileName { get; }

    public string DisplayName { get; }
}
