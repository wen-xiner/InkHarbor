using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using InkHarbor.App.Views;
using InkHarbor.Core.Models;
using InkHarbor.Core.Services;

namespace InkHarbor.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int RecipeCountRecommend = 3;
    private const int RecipeTokenWarnThreshold = 6000;

    /// <summary>
    /// 优先选的模型。实测（2026-10-16）qwen3:4b 是思考版，会先想 3500+ 字、
    /// 把 num_predict 吃光；instruct-2507 是非思考版，直接出正文且快一倍。
    /// </summary>
    private static readonly string[] PreferredModels =
    {
        "qwen3:4b-instruct-2507-q4_K_M",
        "qwen3:4b-instruct-2507",
        "qwen3:4b"
    };

    private readonly WorkspaceService _workspace;
    private readonly CorpusService _corpusService;
    private readonly DraftService _draftService;
    private readonly PresetService _presetService;
    private readonly OllamaClient _ollama;
    private readonly EmbeddingService _embedding;

    private CancellationTokenSource? _generationCts;
    private CancellationTokenSource? _reviseCts;
    private CancellationTokenSource? _suggestCts;
    private bool _isLoading;
    private bool _titleTouched;

    private CorpusItemViewModel? _selectedCorpus;
    private PresetItemViewModel? _selectedPreset;
    private string _suggestResultText = "";
    private bool _isSuggesting;
    private bool _canUndoSuggest;

    /// <summary>点推荐之前的配方，供「撤销」用。</summary>
    private readonly List<string> _recipeBeforeSuggest = new();

    private string _reviseInstruction = "";
    private string _revisionText = "";
    private string _revisionSummary = "";
    private string _revisionWarning = "";
    private string _lastInstruction = "";
    private string _versionPreviewText = "";
    private bool _useCorpusForRevision = true;
    private bool _isRevising;
    private bool _hasRevision;
    private bool _hasVersions;
    private int _revisionRound;
    private VersionItemViewModel? _selectedVersion;

    private string _selectedModel = "";
    private string _title = "";
    private string _outline = "";
    private string _outputText = "";
    private string _editingText = "";
    private string _recipeSummary = "本次配方（还没选范文）";
    private string _recipeWarning = "";
    private string _targetWordsHint = "";
    private string _statusMessage = "";
    private string _ollamaStatusText = "Ollama ○ 未连接";
    private string _draftLabel = "还没有底稿";
    private bool _isOllamaConnected;
    private bool _isGenerating;
    private bool _hasDraft;
    private bool _isEditPanelOpen = true;
    private DraftItemViewModel? _selectedDraft;
    private DraftMeta? _currentMeta;

    public MainViewModel()
    {
        _workspace = new WorkspaceService();
        _corpusService = new CorpusService(_workspace);
        _draftService = new DraftService(_workspace);
        _presetService = new PresetService(_workspace);
        _ollama = new OllamaClient();
        _embedding = new EmbeddingService(_workspace);

        RefreshCommand = new RelayCommand(_ => _ = RefreshAllAsync());
        RemoveChipCommand = new RelayCommand(p => RemoveChip(p as ChipViewModel));
        SavePresetCommand = new RelayCommand(_ => SavePreset());
        GenerateCommand = new RelayCommand(_ => _ = GenerateAsync(), _ => !IsGenerating);
        CancelGenerateCommand = new RelayCommand(_ => _generationCts?.Cancel());
        SaveVersionCommand = new RelayCommand(_ => SaveVersion());
        ConfirmFinalCommand = new RelayCommand(_ => ConfirmFinal());
        DiscardCommand = new RelayCommand(_ => DiscardChanges());
        ToggleEditPanelCommand = new RelayCommand(_ => IsEditPanelOpen = !IsEditPanelOpen);

        // ---- M2：AI 修订 + 版本回退 ----
        ReviseCommand = new RelayCommand(_ => _ = ReviseFromInstructionAsync(), _ => CanStartRevise);
        SelfCheckCommand = new RelayCommand(_ => _ = SelfCheckAsync(), _ => CanStartSelfCheck);
        CancelReviseCommand = new RelayCommand(_ => _reviseCts?.Cancel());
        AdoptRevisionCommand = new RelayCommand(_ => AdoptRevision(), _ => HasRevision);
        DiscardRevisionCommand = new RelayCommand(_ => DiscardRevision(), _ => HasRevision);
        RevertVersionCommand = new RelayCommand(_ => RevertVersion(), _ => CanRevert);

        // ---- M3：范文推荐 / 范文库管理 / 预设管理 ----
        SuggestCorpusCommand = new RelayCommand(_ => _ = SuggestCorpusAsync(), _ => CanSuggest);
        CancelSuggestCommand = new RelayCommand(_ => _suggestCts?.Cancel());
        UndoSuggestCommand = new RelayCommand(_ => UndoSuggest(), _ => CanUndoSuggest);
        NewCorpusCommand = new RelayCommand(_ => NewCorpus());
        EditCorpusCommand = new RelayCommand(_ => EditCorpus(), _ => SelectedCorpus is not null);
        RenameCorpusCommand = new RelayCommand(_ => RenameCorpus(), _ => SelectedCorpus is not null);
        DeleteCorpusCommand = new RelayCommand(_ => DeleteCorpus(), _ => SelectedCorpus is not null);
        OpenCorpusFolderCommand = new RelayCommand(_ => OpenCorpusFolder());
        ApplyPresetCommand = new RelayCommand(_ => ApplyPreset(), _ => SelectedPreset is not null);
        RenamePresetCommand = new RelayCommand(_ => RenamePreset(), _ => SelectedPreset is not null);
        OverwritePresetCommand = new RelayCommand(_ => OverwritePreset(), _ => SelectedPreset is not null);
        DeletePresetCommand = new RelayCommand(_ => DeletePreset(), _ => SelectedPreset is not null);

        _ = RefreshAllAsync();
    }

    // ---- 集合 ----

    public ObservableCollection<CorpusItemViewModel> CorpusItems { get; } = new();

    public ObservableCollection<DraftItemViewModel> DraftItems { get; } = new();

    public ObservableCollection<ChipViewModel> Recipe { get; } = new();

    public ObservableCollection<string> Models { get; } = new();

    /// <summary>版本历史，新的在上面。</summary>
    public ObservableCollection<VersionItemViewModel> Versions { get; } = new();

    /// <summary>配方预设。</summary>
    public ObservableCollection<PresetItemViewModel> Presets { get; } = new();

    // ---- 命令 ----

    public RelayCommand RefreshCommand { get; }

    public RelayCommand RemoveChipCommand { get; }

    public RelayCommand SavePresetCommand { get; }

    public RelayCommand GenerateCommand { get; }

    public RelayCommand CancelGenerateCommand { get; }

    public RelayCommand SaveVersionCommand { get; }

    public RelayCommand ConfirmFinalCommand { get; }

    public RelayCommand DiscardCommand { get; }

    public RelayCommand ToggleEditPanelCommand { get; }

    public RelayCommand ReviseCommand { get; }

    public RelayCommand SelfCheckCommand { get; }

    public RelayCommand CancelReviseCommand { get; }

    public RelayCommand AdoptRevisionCommand { get; }

    public RelayCommand DiscardRevisionCommand { get; }

    public RelayCommand RevertVersionCommand { get; }

    // ---- M3 ----

    public RelayCommand SuggestCorpusCommand { get; }

    public RelayCommand CancelSuggestCommand { get; }

    public RelayCommand UndoSuggestCommand { get; }

    public RelayCommand NewCorpusCommand { get; }

    public RelayCommand EditCorpusCommand { get; }

    public RelayCommand RenameCorpusCommand { get; }

    public RelayCommand DeleteCorpusCommand { get; }

    public RelayCommand OpenCorpusFolderCommand { get; }

    public RelayCommand ApplyPresetCommand { get; }

    public RelayCommand RenamePresetCommand { get; }

    public RelayCommand OverwritePresetCommand { get; }

    public RelayCommand DeletePresetCommand { get; }

    // ---- 属性 ----

    public string WorkspaceRoot => _workspace.Root;

    public string SelectedModel
    {
        get => _selectedModel;
        set => SetProperty(ref _selectedModel, value);
    }

    public string Title
    {
        get => _title;
        set
        {
            _titleTouched = true;
            SetProperty(ref _title, value);
        }
    }

    public string Outline
    {
        get => _outline;
        set
        {
            if (!SetProperty(ref _outline, value))
            {
                return;
            }

            var target = OutlineParser.ExtractTargetWords(value);
            TargetWordsHint = target is null
                ? $"目标字数：没写，默认按约 {OutlineParser.DefaultTargetWords} 字走（在大纲里写一句「约1500字」可覆盖）"
                : $"目标字数：约 {target} 字";

            if (!_titleTouched)
            {
                _title = SuggestTitleFromOutline();
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }

    public string EditingText
    {
        get => _editingText;
        set => SetProperty(ref _editingText, value);
    }

    public string RecipeSummary
    {
        get => _recipeSummary;
        private set => SetProperty(ref _recipeSummary, value);
    }

    public string RecipeWarning
    {
        get => _recipeWarning;
        private set
        {
            if (SetProperty(ref _recipeWarning, value))
            {
                OnPropertyChanged(nameof(HasRecipeWarning));
            }
        }
    }

    public bool HasRecipeWarning => !string.IsNullOrEmpty(_recipeWarning);

    public string TargetWordsHint
    {
        get => _targetWordsHint;
        private set => SetProperty(ref _targetWordsHint, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string OllamaStatusText
    {
        get => _ollamaStatusText;
        private set => SetProperty(ref _ollamaStatusText, value);
    }

    public string DraftLabel
    {
        get => _draftLabel;
        private set => SetProperty(ref _draftLabel, value);
    }

    public bool IsOllamaConnected
    {
        get => _isOllamaConnected;
        private set => SetProperty(ref _isOllamaConnected, value);
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        private set
        {
            if (SetProperty(ref _isGenerating, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                GenerateCommand.RaiseCanExecuteChanged();
                SuggestCorpusCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !IsGenerating && !IsRevising && !IsSuggesting;

    /// <summary>「展开修改」那块面板的开合。</summary>
    public bool IsEditPanelOpen
    {
        get => _isEditPanelOpen;
        private set
        {
            if (SetProperty(ref _isEditPanelOpen, value))
            {
                OnPropertyChanged(nameof(EditPanelToggleText));
            }
        }
    }

    public string EditPanelToggleText => IsEditPanelOpen ? "收起修改 ▴" : "展开修改 ▾";

    public bool HasDraft
    {
        get => _hasDraft;
        private set
        {
            if (SetProperty(ref _hasDraft, value))
            {
                RaiseReviseState();
            }
        }
    }

    public DraftItemViewModel? SelectedDraft
    {
        get => _selectedDraft;
        set => SetProperty(ref _selectedDraft, value);
    }

    // ---- M2：AI 修订 ----

    public string ReviseInstruction
    {
        get => _reviseInstruction;
        set
        {
            if (SetProperty(ref _reviseInstruction, value))
            {
                ReviseCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>修订时带不带范文一起发。默认带——4B 重写整篇时靠它压住文风。</summary>
    public bool UseCorpusForRevision
    {
        get => _useCorpusForRevision;
        set => SetProperty(ref _useCorpusForRevision, value);
    }

    /// <summary>内存里的修订稿。没点「采纳」就不会落盘。</summary>
    public string RevisionText
    {
        get => _revisionText;
        private set => SetProperty(ref _revisionText, value);
    }

    public bool HasRevision
    {
        get => _hasRevision;
        private set
        {
            if (SetProperty(ref _hasRevision, value))
            {
                AdoptRevisionCommand.RaiseCanExecuteChanged();
                DiscardRevisionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRevising
    {
        get => _isRevising;
        private set
        {
            if (SetProperty(ref _isRevising, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RaiseReviseState();
                SuggestCorpusCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RevisionSummary
    {
        get => _revisionSummary;
        private set => SetProperty(ref _revisionSummary, value);
    }

    public string RevisionWarning
    {
        get => _revisionWarning;
        private set
        {
            if (SetProperty(ref _revisionWarning, value))
            {
                OnPropertyChanged(nameof(HasRevisionWarning));
            }
        }
    }

    public bool HasRevisionWarning => !string.IsNullOrEmpty(_revisionWarning);

    private bool CanStartRevise =>
        HasDraft && !IsGenerating && !IsRevising && !string.IsNullOrWhiteSpace(ReviseInstruction);

    private bool CanStartSelfCheck => HasDraft && !IsGenerating && !IsRevising;

    private void RaiseReviseState()
    {
        ReviseCommand.RaiseCanExecuteChanged();
        SelfCheckCommand.RaiseCanExecuteChanged();
    }

    // ---- M2：版本历史 ----

    public VersionItemViewModel? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetProperty(ref _selectedVersion, value))
            {
                return;
            }

            VersionPreviewText = value is null || _currentMeta is null
                ? ""
                : _draftService.LoadVersionText(_currentMeta.Title, value.Number);
            OnPropertyChanged(nameof(CanRevert));
            OnPropertyChanged(nameof(RevertHintText));
            RevertVersionCommand.RaiseCanExecuteChanged();
        }
    }

    public string VersionPreviewText
    {
        get => _versionPreviewText;
        private set => SetProperty(ref _versionPreviewText, value);
    }

    public bool HasVersions
    {
        get => _hasVersions;
        private set => SetProperty(ref _hasVersions, value);
    }

    /// <summary>当前版本不能回退到自己。</summary>
    public bool CanRevert => _currentMeta is not null && SelectedVersion is { IsCurrent: false };

    public string RevertHintText => SelectedVersion is null
        ? ""
        : SelectedVersion.IsCurrent
            ? "这已经就是当前版本。"
            : $"回退＝把 v{SelectedVersion.Number} 的内容复制成一个新版本，历史一个都不删。";

    // ---- M3：按大纲推荐范文 ----

    /// <summary>生成 / 修订 / 正在推荐时不给点，避免几路抢占同一个 Ollama。</summary>
    private bool CanSuggest => !IsSuggesting && !IsGenerating && !IsRevising;

    public bool IsSuggesting
    {
        get => _isSuggesting;
        private set
        {
            if (SetProperty(ref _isSuggesting, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                SuggestCorpusCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>推荐结果那一行（相似度、进度、缺模型的提示都写这儿）。</summary>
    public string SuggestResultText
    {
        get => _suggestResultText;
        private set
        {
            if (SetProperty(ref _suggestResultText, value))
            {
                OnPropertyChanged(nameof(HasSuggestResult));
            }
        }
    }

    public bool HasSuggestResult => _suggestResultText.Length > 0;

    /// <summary>上一轮推荐还能不能撤销。</summary>
    public bool CanUndoSuggest
    {
        get => _canUndoSuggest;
        private set
        {
            if (SetProperty(ref _canUndoSuggest, value))
            {
                UndoSuggestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // ---- M3：范文库 / 预设的选中项 ----

    public CorpusItemViewModel? SelectedCorpus
    {
        get => _selectedCorpus;
        set
        {
            if (SetProperty(ref _selectedCorpus, value))
            {
                EditCorpusCommand.RaiseCanExecuteChanged();
                RenameCorpusCommand.RaiseCanExecuteChanged();
                DeleteCorpusCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public PresetItemViewModel? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                ApplyPresetCommand.RaiseCanExecuteChanged();
                RenamePresetCommand.RaiseCanExecuteChanged();
                OverwritePresetCommand.RaiseCanExecuteChanged();
                DeletePresetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // ---- 刷新 ----

    private async Task RefreshAllAsync()
    {
        _isLoading = true;
        try
        {
            _workspace.EnsureDirectories();

            ReloadCorpus();
            RefreshDrafts();
            RefreshPresets();

            IsOllamaConnected = await _ollama.PingAsync();

            Models.Clear();
            if (IsOllamaConnected)
            {
                foreach (var model in await _ollama.ListModelsAsync())
                {
                    Models.Add(model);
                }
            }

            if (Models.Count == 0)
            {
                Models.Add("qwen3:4b");
            }

            if (string.IsNullOrWhiteSpace(SelectedModel) || !Models.Contains(SelectedModel))
            {
                SelectedModel = PreferredModels.FirstOrDefault(Models.Contains) ?? Models[0];
            }

            OllamaStatusText = IsOllamaConnected ? "Ollama ● 已连接" : "Ollama ○ 未连接";

            StatusMessage = IsOllamaConnected
                ? $"工作区 {_workspace.Root}｜范文 {CorpusItems.Count} 篇｜作品 {DraftItems.Count} 篇｜模型 {SelectedModel}"
                : $"Ollama 未连接（localhost:11434）｜工作区 {_workspace.Root}｜范文 {CorpusItems.Count} 篇";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RefreshDrafts()
    {
        var keep = SelectedDraft?.Title;

        DraftItems.Clear();
        foreach (var summary in _draftService.ListDrafts())
        {
            DraftItems.Add(new DraftItemViewModel(summary));
        }

        if (keep is not null)
        {
            var same = DraftItems.FirstOrDefault(d => d.Title == keep);
            if (same is not null)
            {
                SelectedDraft = same;
            }
        }
    }

    /// <summary>
    /// 重扫范文库。默认按当前配方里记着的文件名恢复勾选；
    /// 传了 <paramref name="selectNames"/> 就按它来（改名那种"名字变了但还想保持勾选"的场景用）。
    /// </summary>
    private void ReloadCorpus(IReadOnlyCollection<string>? selectNames = null)
    {
        // 注意：要在清空列表之前先把配方读出来，配方条是唯一可靠的勾选记录。
        var wanted = selectNames is null
            ? Recipe.Select(c => c.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(selectNames, StringComparer.OrdinalIgnoreCase);

        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            CorpusItems.Clear();
            foreach (var file in _corpusService.Scan())
            {
                var vm = new CorpusItemViewModel(file, OnCorpusToggled);
                CorpusItems.Add(vm);
                vm.IsSelected = wanted.Contains(file.FileName);
            }
        }
        finally
        {
            _isLoading = wasLoading;
        }

        SyncRecipe();
    }

    private void RefreshPresets()
    {
        var keep = SelectedPreset?.Name;

        Presets.Clear();
        foreach (var preset in _presetService.LoadAll())
        {
            Presets.Add(new PresetItemViewModel(preset));
        }

        if (keep is not null)
        {
            SelectedPreset = Presets.FirstOrDefault(p => p.Name == keep);
        }
    }

    // ---- 配方 ----

    private void OnCorpusToggled(CorpusItemViewModel item)
    {
        if (_isLoading)
        {
            return;
        }

        SyncRecipe();
    }

    private void SyncRecipe()
    {
        var selected = CorpusItems.Where(c => c.IsSelected).ToList();

        Recipe.Clear();
        foreach (var item in selected)
        {
            Recipe.Add(new ChipViewModel(item.File.FileName, item.DisplayName));
        }

        var tokens = selected.Sum(c => c.File.EstimatedTokens);
        RecipeSummary = selected.Count == 0
            ? "本次配方（还没选范文）"
            : $"本次配方（已选 {selected.Count} 篇 · 约 {tokens} token）";

        if (selected.Count > RecipeCountRecommend)
        {
            RecipeWarning = $"范文选了 {selected.Count} 篇，偏多，4B 容易抄串，建议 2~3 篇。";
        }
        else if (tokens > RecipeTokenWarnThreshold)
        {
            RecipeWarning = $"配方约 {tokens} token，上下文偏重，生成会明显变慢。";
        }
        else
        {
            RecipeWarning = "";
        }

        GenerateCommand.RaiseCanExecuteChanged();
    }

    private void RemoveChip(ChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        var item = CorpusItems.FirstOrDefault(c => c.File.FileName == chip.FileName);
        if (item is not null)
        {
            item.IsSelected = false;
        }
    }

    private void SavePreset()
    {
        if (Recipe.Count == 0)
        {
            Warn("配方是空的，先勾几篇范文。");
            return;
        }

        var name = InputDialog.Show("存为预设", "给这个配方起个名字：", $"配方 {DateTime.Now:MMdd-HHmm}");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _presetService.Save(name.Trim(), Recipe.Select(c => c.FileName));
        RefreshPresets();
        SelectedPreset = Presets.FirstOrDefault(p => p.Name == name.Trim());
        StatusMessage = $"预设「{name.Trim()}」已存进 presets\\，左栏「预设」页签里能改名、覆盖、删掉。";
    }

    // ---- M3：按大纲推荐范文 ----

    private async Task SuggestCorpusAsync()
    {
        if (IsSuggesting)
        {
            return;
        }

        if (!IsOllamaConnected)
        {
            Warn("Ollama 没连上（localhost:11434）。推荐要靠本机算向量，不接任何云端。");
            return;
        }

        if (string.IsNullOrWhiteSpace(Outline))
        {
            Warn("先写情节大纲——推荐是照着大纲去找文风的。");
            return;
        }

        if (CorpusItems.Count == 0)
        {
            Warn("范文库是空的。先往 corpus\\ 里放几篇，或者点「新建范文」。");
            return;
        }

        const string model = EmbeddingService.DefaultModel;

        _suggestCts = new CancellationTokenSource();
        IsSuggesting = true;
        SuggestResultText = "准备中…";

        try
        {
            if (!await _embedding.IsModelReadyAsync(model, _suggestCts.Token))
            {
                SuggestResultText = $"缺嵌入模型 {model}：先在终端跑 ollama pull {model}";
                Warn($"Ollama 里没有嵌入模型「{model}」。\n\n先在终端跑一条：\n    ollama pull {model}\n\n"
                     + "装好之后点顶栏「刷新」，再回来点推荐。");
                return;
            }

            var files = CorpusItems.Select(c => c.File).ToList();
            var progress = new Progress<string>(text => SuggestResultText = text);

            var suggestions = await _embedding.SuggestAsync(
                model,
                files,
                _corpusService.ReadText,
                Outline,
                RecipeCountRecommend,
                progress,
                _suggestCts.Token);

            if (suggestions.Count == 0)
            {
                SuggestResultText = "没能推荐出结果。";
                Warn("没推荐出结果。检查一下范文库里是不是没有正文。");
                return;
            }

            // 记下当前配方，供「撤销」回到推荐之前。
            _recipeBeforeSuggest.Clear();
            _recipeBeforeSuggest.AddRange(Recipe.Select(c => c.FileName));
            CanUndoSuggest = true;

            var picked = suggestions.Select(s => s.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _isLoading = true;
            foreach (var item in CorpusItems)
            {
                item.IsSelected = picked.Contains(item.File.FileName);
            }

            _isLoading = false;
            SyncRecipe();

            var detail = string.Join(" ｜ ", suggestions.Select(s => $"{s.DisplayName} {s.Score:F3}"));
            SuggestResultText = $"推荐 {suggestions.Count} 篇：{detail}";
            StatusMessage = $"已照大纲换成本次配方（{suggestions.Count} 篇）。不满意点「撤销」，或者手动勾几下。";
        }
        catch (OperationCanceledException)
        {
            SuggestResultText = "已取消。";
            StatusMessage = "已取消推荐，配方没动。";
        }
        catch (Exception ex)
        {
            SuggestResultText = "推荐失败。";
            Warn("推荐失败：" + ex.Message);
        }
        finally
        {
            IsSuggesting = false;
            _suggestCts?.Dispose();
            _suggestCts = null;
        }
    }

    private void UndoSuggest()
    {
        if (!CanUndoSuggest)
        {
            return;
        }

        var wanted = new HashSet<string>(_recipeBeforeSuggest, StringComparer.OrdinalIgnoreCase);
        _isLoading = true;
        foreach (var item in CorpusItems)
        {
            item.IsSelected = wanted.Contains(item.File.FileName);
        }

        _isLoading = false;
        SyncRecipe();

        CanUndoSuggest = false;
        SuggestResultText = "";
        StatusMessage = "已把配方恢复成推荐之前的样子。";
    }

    // ---- M3：范文库管理 ----

    private void NewCorpus()
    {
        var window = CorpusEditWindow.ForCreate();
        if (window.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var file = _corpusService.Create(window.FileDisplayName, window.BodyText, window.FileExtension);
            ReloadCorpus();
            SelectedCorpus = FindCorpusItem(file.FileName);
            StatusMessage = $"已新增范文 {file.FileName}（{file.WordCount} 字，约 {file.EstimatedTokens} token）。";
        }
        catch (Exception ex)
        {
            Warn("新建失败：" + ex.Message);
        }
    }

    private void EditCorpus()
    {
        if (SelectedCorpus is null)
        {
            Warn("先在列表里点一篇范文。");
            return;
        }

        var file = SelectedCorpus.File;

        try
        {
            var window = CorpusEditWindow.ForEdit(file, _corpusService.ReadText(file));
            if (window.ShowDialog() != true)
            {
                return;
            }

            var saved = _corpusService.SaveText(file, window.BodyText);
            ReloadCorpus();
            SelectedCorpus = FindCorpusItem(saved.FileName);
            StatusMessage = $"已保存 {saved.FileName}（{saved.WordCount} 字）。它的向量下次推荐时自动重算。";
        }
        catch (Exception ex)
        {
            Warn("保存失败：" + ex.Message);
        }
    }

    private void RenameCorpus()
    {
        if (SelectedCorpus is null)
        {
            Warn("先在列表里点一篇范文。");
            return;
        }

        var file = SelectedCorpus.File;
        var newName = InputDialog.Show(
            "重命名范文",
            "文件名就是标签，改这里等于改标签。不用带扩展名：",
            file.DisplayName);

        if (newName is null || newName == file.DisplayName)
        {
            return;
        }

        try
        {
            var renamed = _corpusService.Rename(file, newName);
            var touched = _draftService.ReplaceCorpusReference(file.FileName, renamed.FileName);

            // 文件名变了，配方里的勾选得跟着搬过去，否则配方会莫名其妙少一篇。
            var keep = Recipe
                .Select(c => c.FileName.Equals(file.FileName, StringComparison.OrdinalIgnoreCase)
                    ? renamed.FileName
                    : c.FileName)
                .ToList();

            ReloadCorpus(keep);
            SelectedCorpus = FindCorpusItem(renamed.FileName);

            StatusMessage = touched == 0
                ? $"已改名为 {renamed.FileName}。"
                : $"已改名为 {renamed.FileName}，同时帮 {touched} 部作品更新了引用记录。";
        }
        catch (Exception ex)
        {
            Warn("重命名失败：" + ex.Message);
        }
    }

    private void DeleteCorpus()
    {
        if (SelectedCorpus is null)
        {
            Warn("先在列表里点一篇范文。");
            return;
        }

        var file = SelectedCorpus.File;
        var referenced = _draftService.CountCorpusReferences(file.FileName);

        var message = $"把「{file.DisplayName}」移出范文库？\n\n"
                      + "不是真删——会移到 corpus\\_trash\\ 里，你随时能去那儿捞回来。";

        if (referenced > 0)
        {
            message += $"\n\n有 {referenced} 部作品的记录里引用过它。那些老稿子本身不受影响，"
                       + "只是以后打开时会少勾这一篇。";
        }

        var answer = MessageBox.Show(message, "移出范文库", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _corpusService.SoftDelete(file);
            ReloadCorpus();
            SelectedCorpus = null;
            StatusMessage = $"已把 {file.FileName} 移到 corpus\\_trash\\。想恢复就去那儿拖回来再点「刷新」。";
        }
        catch (Exception ex)
        {
            Warn("移出失败：" + ex.Message);
        }
    }

    private void OpenCorpusFolder()
    {
        try
        {
            Directory.CreateDirectory(_workspace.CorpusDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = _workspace.CorpusDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Warn("打不开文件夹：" + ex.Message);
        }
    }

    private CorpusItemViewModel? FindCorpusItem(string fileName) =>
        CorpusItems.FirstOrDefault(c => c.File.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

    // ---- M3：预设管理 ----

    private void ApplyPreset()
    {
        if (SelectedPreset is null)
        {
            Warn("先在列表里点一个预设。");
            return;
        }

        var wanted = SelectedPreset.CorpusFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = SelectedPreset.CorpusFiles.Count(name =>
            CorpusItems.All(c => !c.File.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)));

        _isLoading = true;
        foreach (var item in CorpusItems)
        {
            item.IsSelected = wanted.Contains(item.File.FileName);
        }

        _isLoading = false;
        SyncRecipe();

        CanUndoSuggest = false;
        StatusMessage = missing == 0
            ? $"已套用预设「{SelectedPreset.Name}」（{SelectedPreset.CorpusFiles.Count} 篇）。"
            : $"已套用预设「{SelectedPreset.Name}」，其中 {missing} 篇在库里找不到了（可能被改名或移走）。";
    }

    private void RenamePreset()
    {
        if (SelectedPreset is null)
        {
            Warn("先在列表里点一个预设。");
            return;
        }

        var oldName = SelectedPreset.Name;
        var newName = InputDialog.Show("重命名预设", "新名字：", oldName);
        if (newName is null || newName == oldName)
        {
            return;
        }

        try
        {
            var renamed = _presetService.Rename(oldName, newName);
            RefreshPresets();
            SelectedPreset = Presets.FirstOrDefault(p => p.Name == renamed.Name);
            StatusMessage = $"预设「{oldName}」已改名为「{renamed.Name}」。";
        }
        catch (Exception ex)
        {
            Warn("改名失败：" + ex.Message);
        }
    }

    private void OverwritePreset()
    {
        if (SelectedPreset is null)
        {
            Warn("先在列表里点一个预设。");
            return;
        }

        if (Recipe.Count == 0)
        {
            Warn("当前配方是空的，没法覆盖。");
            return;
        }

        var name = SelectedPreset.Name;
        var answer = MessageBox.Show(
            $"用当前配方（{Recipe.Count} 篇）覆盖预设「{name}」？\n\n只动这个预设文件，范文和稿子都不受影响。",
            "覆盖预设", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        _presetService.Save(name, Recipe.Select(c => c.FileName));
        RefreshPresets();
        SelectedPreset = Presets.FirstOrDefault(p => p.Name == name);
        StatusMessage = $"预设「{name}」已用当前配方覆盖。";
    }

    private void DeletePreset()
    {
        if (SelectedPreset is null)
        {
            Warn("先在列表里点一个预设。");
            return;
        }

        var name = SelectedPreset.Name;
        var answer = MessageBox.Show(
            $"删掉预设「{name}」？\n\n只删这条配方记录，范文和稿子都不动。",
            "删除预设", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        _presetService.Delete(name);
        RefreshPresets();
        StatusMessage = $"预设「{name}」已删除。";
    }

    // ---- 生成 ----

    private async Task GenerateAsync()
    {
        if (IsGenerating)
        {
            return;
        }

        if (!IsOllamaConnected)
        {
            Warn("Ollama 没连上（localhost:11434）。软件只连本机 Ollama，不接任何云端。");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedModel))
        {
            Warn("没有可用模型，先跑一次 ollama pull qwen3:4b。");
            return;
        }

        if (Recipe.Count == 0)
        {
            Warn("先到左栏「范文库」勾 2~3 篇范文。");
            return;
        }

        if (string.IsNullOrWhiteSpace(Outline))
        {
            Warn("情节大纲还是空的。");
            return;
        }

        var corpusNames = Recipe.Select(c => c.FileName).ToList();
        var corpusTexts = new List<string>();
        foreach (var name in corpusNames)
        {
            var item = CorpusItems.FirstOrDefault(c => c.File.FileName == name);
            if (item is not null)
            {
                corpusTexts.Add(_corpusService.ReadText(item.File));
            }
        }

        var targetWords = OutlineParser.ResolveTargetWords(Outline);
        var prompt = PromptBuilder.BuildGenerationPrompt(corpusTexts, Outline, targetWords);
        var options = new GenerationOptions
        {
            // Qwen3 思考版会先想一大段（本机实测思考 3500+ 字），思考 token 也算在
            // num_predict 里，所以余量给足；非思考版用不到这么多也只是留白。
            NumPredict = Math.Clamp((targetWords * 2) + 2048, 2048, 8192)
        };
        var stats = new GenerationStats();

        var draftTitle = string.IsNullOrWhiteSpace(Title) ? SuggestTitleFromOutline() : Title.Trim();
        Title = draftTitle;

        _generationCts = new CancellationTokenSource();
        var buffer = new StringBuilder();
        var lastFlush = 0L;

        IsGenerating = true;
        OutputText = "";
        EditingText = "";
        StatusMessage = $"{SelectedModel} 正在思考…（Qwen3 会先想一段再动笔）";

        var started = false;

        try
        {
            await foreach (var delta in _ollama.ChatStreamAsync(
                               SelectedModel,
                               PromptBuilder.DefaultSystemPrompt,
                               prompt,
                               options,
                               stats,
                               _generationCts.Token))
            {
                if (!started)
                {
                    started = true;
                    StatusMessage = $"生成中（{SelectedModel} · 目标约 {targetWords} 字）…";
                }

                buffer.Append(delta);

                // 每个 token 都刷界面会把 UI 拖死，60ms 刷一次。
                var now = Environment.TickCount64;
                if (now - lastFlush > 60)
                {
                    lastFlush = now;
                    OutputText = buffer.ToString();
                }
            }

            var text = buffer.ToString().Trim();
            OutputText = text;

            if (text.Length == 0)
            {
                Warn("模型一个字都没吐出来。检查模型是否可用，或把 num_ctx 调小。");
                return;
            }

            _currentMeta = _draftService.CreateDraft(draftTitle, Outline, SelectedModel, corpusNames, text);
            EditingText = text;
            HasDraft = true;
            DraftLabel = $"{_currentMeta.Title} v1 · 已落盘，只读";
            ResetRevision();
            RefreshVersions();
            RefreshDrafts();
            SelectDraftInList(_currentMeta.Title);

            var words = TextMetrics.CountWords(text);
            var statsText = stats.PromptTokens is null
                ? ""
                : $"（输入 {stats.PromptTokens} token / 输出 {stats.OutputTokens} token，含思考 {stats.ThinkingChars} 字 / {stats.TokensPerSecond:F1} tok/s）";

            if (stats.Truncated)
            {
                StatusMessage = $"⚠ 写出来的稿被 num_predict 截断了（{words} 字，done_reason=length）。"
                                + "底稿已落盘但可能不完整：把目标字数写小些，或调大 GenerationOptions.NumPredict。";
            }
            else
            {
                StatusMessage = $"生成完成，{words} 字{statsText}，底稿 v1 已自动落盘。「展开修改」可以手动改。";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已终止本轮生成，没有落盘。";
        }
        catch (Exception ex)
        {
            Warn("生成失败：" + ex.Message);
            StatusMessage = "生成失败。";
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    // ---- 修改与入终稿 ----

    private void SaveVersion()
    {
        if (_currentMeta is null)
        {
            Warn("还没有底稿，先生成一版。");
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingText))
        {
            Warn("编辑区是空的。");
            return;
        }

        var current = _draftService.LoadVersionText(_currentMeta.Title, _currentMeta.CurrentVersion);
        if (string.Equals(EditingText, current, StringComparison.Ordinal))
        {
            Warn("内容和当前版本一样，不用存新版本。");
            return;
        }

        _currentMeta = _draftService.AddVersion(_currentMeta, EditingText, VersionSource.Manual);
        HasDraft = true;
        DraftLabel = $"{_currentMeta.Title} v{_currentMeta.CurrentVersion} · 已存新版本，老版本原样留着";
        RefreshVersions();
        RefreshDrafts();
        StatusMessage = $"已存成 v{_currentMeta.CurrentVersion}，老版本没动。";
    }

    private void ConfirmFinal()
    {
        if (_currentMeta is null)
        {
            Warn("还没有底稿，不能入终稿。");
            return;
        }

        var current = _draftService.LoadVersionText(_currentMeta.Title, _currentMeta.CurrentVersion);

        // 编辑区要是改了还没存，先落一个新版本再入终稿，别让改动莫名丢掉。
        if (!string.IsNullOrWhiteSpace(EditingText) && !string.Equals(EditingText, current, StringComparison.Ordinal))
        {
            _currentMeta = _draftService.AddVersion(_currentMeta, EditingText, VersionSource.Manual, "入终稿前保存");
        }

        var text = _draftService.LoadVersionText(_currentMeta.Title, _currentMeta.CurrentVersion);
        var path = _draftService.SaveAsFinal(_currentMeta, text);

        HasDraft = true;
        DraftLabel = $"{_currentMeta.Title} · 已入终稿";
        RefreshVersions();
        RefreshDrafts();
        StatusMessage = $"已存入终稿：{path}（原终稿从不覆盖，同名自动加序号）";
    }

    private void DiscardChanges()
    {
        if (_currentMeta is null)
        {
            return;
        }

        var text = _draftService.LoadVersionText(_currentMeta.Title, _currentMeta.CurrentVersion);
        EditingText = text;
        OutputText = text;
        ResetRevision();
        StatusMessage = $"已丢掉本轮未保存的改动，回到磁盘上的 v{_currentMeta.CurrentVersion}（已存的版本不删）。";
    }

    // ---- M2：AI 修订 ----

    private Task ReviseFromInstructionAsync()
    {
        var instruction = ReviseInstruction.Trim();
        if (instruction.Length == 0)
        {
            Warn("先写一条修订指令，比如「第二段压短，节奏加快」。");
            return Task.CompletedTask;
        }

        return RunRevisionAsync(instruction, isSelfCheck: false);
    }

    private Task SelfCheckAsync() => RunRevisionAsync(PromptBuilder.BuildSelfCheckPrompt(), isSelfCheck: true);

    /// <summary>
    /// 跑一轮修订。结果只进内存（<see cref="RevisionText"/>），点了「采纳」才落盘。
    /// 修订的底稿：已经有修订稿就接着在它上面改（多轮迭代），否则用编辑框里的文字。
    /// </summary>
    private async Task RunRevisionAsync(string instruction, bool isSelfCheck)
    {
        if (IsRevising || IsGenerating)
        {
            return;
        }

        if (!IsOllamaConnected)
        {
            Warn("Ollama 没连上（localhost:11434）。软件只连本机 Ollama，不接任何云端。");
            return;
        }

        if (_currentMeta is null)
        {
            Warn("还没有底稿，先生成一版。");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedModel))
        {
            Warn("没有可用模型。");
            return;
        }

        var baseText = HasRevision && !string.IsNullOrWhiteSpace(RevisionText)
            ? RevisionText
            : !string.IsNullOrWhiteSpace(EditingText)
                ? EditingText
                : _draftService.LoadVersionText(_currentMeta.Title, _currentMeta.CurrentVersion);

        if (string.IsNullOrWhiteSpace(baseText))
        {
            Warn("没有可修订的正文。");
            return;
        }

        var corpusTexts = new List<string>();
        if (UseCorpusForRevision)
        {
            foreach (var name in _currentMeta.CorpusFiles)
            {
                var item = CorpusItems.FirstOrDefault(c => c.File.FileName == name);
                if (item is not null)
                {
                    corpusTexts.Add(_corpusService.ReadText(item.File));
                }
            }
        }

        var prompt = PromptBuilder.BuildRevisionPrompt(corpusTexts, baseText, instruction);
        var baseWords = TextMetrics.CountWords(baseText);
        var options = new GenerationOptions
        {
            NumPredict = Math.Clamp((baseWords * 2) + 2048, 2048, 8192)
        };
        var stats = new GenerationStats();

        // 这一轮跑挂了（终止/空产出）就把上一版修订稿原样还回去，别让它白白丢掉。
        var backupText = RevisionText;
        var backupHasRevision = HasRevision;

        _lastInstruction = instruction;
        _reviseCts = new CancellationTokenSource();
        var buffer = new StringBuilder();
        var lastFlush = 0L;

        IsRevising = true;
        RevisionWarning = "";
        RevisionSummary = "模型正在按指令改稿…";
        StatusMessage = $"{SelectedModel} 正在修订（{(corpusTexts.Count > 0 ? $"带范文 {corpusTexts.Count} 篇" : "不带范文")}）…";

        var started = false;

        try
        {
            await foreach (var delta in _ollama.ChatStreamAsync(
                               SelectedModel,
                               PromptBuilder.DefaultSystemPrompt,
                               prompt,
                               options,
                               stats,
                               _reviseCts.Token))
            {
                if (!started)
                {
                    started = true;
                    StatusMessage = isSelfCheck ? $"自查轮运行中（{SelectedModel}）…" : $"修订中（{SelectedModel}）…";
                    RevisionSummary = "修订中…";
                }

                buffer.Append(delta);

                // 和生成一样，60ms 刷一次界面，别把 UI 拖死。
                var now = Environment.TickCount64;
                if (now - lastFlush > 60)
                {
                    lastFlush = now;
                    RevisionText = buffer.ToString();
                }
            }

            var result = buffer.ToString().Trim();
            if (result.Length == 0)
            {
                RevisionText = backupText;
                HasRevision = backupHasRevision;
                RevisionSummary = backupHasRevision ? "这一轮没有产出，上一版修订稿还在。" : "";
                Warn("模型一个字都没吐出来，这一轮没有产出。");
                StatusMessage = "修订没有产出。";
                return;
            }

            _revisionRound++;
            RevisionText = result;
            HasRevision = true;

            var resultWords = TextMetrics.CountWords(result);
            var deltaWords = resultWords - baseWords;
            RevisionSummary = $"第 {_revisionRound} 轮：上一版 {baseWords} 字 → 这版 {resultWords} 字（{deltaWords:+#;-#;0}）"
                              + (stats.PromptTokens is null ? "" : $"｜{stats.TokensPerSecond:F1} tok/s");
            RevisionWarning = BuildRevisionWarning(stats, baseWords, resultWords);

            StatusMessage = $"修订完成（第 {_revisionRound} 轮）。满意就点「采纳」，不满意可以再给一条指令接着改。";
        }
        catch (OperationCanceledException)
        {
            RevisionText = backupText;
            HasRevision = backupHasRevision;
            RevisionSummary = backupHasRevision ? "已终止，上一版修订稿还在。" : "";
            StatusMessage = "已终止本轮修订，磁盘没动。";
        }
        catch (Exception ex)
        {
            RevisionText = backupText;
            HasRevision = backupHasRevision;
            RevisionSummary = backupHasRevision ? "这一轮失败了，上一版修订稿还在。" : "";
            Warn("修订失败：" + ex.Message);
            StatusMessage = "修订失败。";
        }
        finally
        {
            IsRevising = false;
            _reviseCts?.Dispose();
            _reviseCts = null;
        }
    }

    /// <summary>
    /// 4B 改稿的两个常见事故：被 num_predict 截断、整段漏掉或自我发挥。
    /// 只提醒不拦——采纳与否的决定权留给人。
    /// </summary>
    private static string BuildRevisionWarning(GenerationStats stats, int baseWords, int resultWords)
    {
        var warnings = new List<string>();

        if (stats.Truncated)
        {
            warnings.Add($"⚠ 修订稿被 num_predict 截断了（done_reason=length，{resultWords} 字），末尾可能不完整。");
        }

        if (baseWords > 0 && resultWords < baseWords * 0.6)
        {
            warnings.Add($"⚠ 修订稿比上一版少了 {baseWords - resultWords} 字，可能漏段了，采纳前先看一眼。");
        }
        else if (baseWords > 0 && resultWords > baseWords * 1.8)
        {
            warnings.Add($"⚠ 修订稿比上一版多了 {resultWords - baseWords} 字，可能自我发挥了。");
        }

        return string.Join("\n", warnings);
    }

    /// <summary>采纳：把修订稿写进编辑框，并立刻落一个新版本（来源 ai，记下当初那条指令）。</summary>
    private void AdoptRevision()
    {
        if (_currentMeta is null || !HasRevision || string.IsNullOrWhiteSpace(RevisionText))
        {
            return;
        }

        try
        {
            var note = _revisionRound > 1 ? $"AI 修订（共 {_revisionRound} 轮）" : "AI 修订";
            _currentMeta = _draftService.AddVersion(
                _currentMeta,
                RevisionText,
                VersionSource.Ai,
                note,
                SelectedModel,
                instruction: _lastInstruction);

            EditingText = RevisionText;
            OutputText = RevisionText;
            HasDraft = true;
            DraftLabel = $"{_currentMeta.Title} v{_currentMeta.CurrentVersion} · AI 修订已采纳";
            ResetRevision();
            RefreshVersions();
            RefreshDrafts();
            StatusMessage = $"已采纳，落成 v{_currentMeta.CurrentVersion}（来源 ai）。原来那些版本一个没动。";
        }
        catch (Exception ex)
        {
            Warn("采纳失败：" + ex.Message);
        }
    }

    /// <summary>丢弃修订稿。磁盘上什么都没发生过。</summary>
    private void DiscardRevision()
    {
        if (!HasRevision)
        {
            return;
        }

        ResetRevision();
        StatusMessage = "已丢弃这版修订稿，磁盘上什么都没变。";
    }

    private void ResetRevision()
    {
        RevisionText = "";
        RevisionSummary = "";
        RevisionWarning = "";
        HasRevision = false;
        _revisionRound = 0;
        _lastInstruction = "";
    }

    // ---- M2：版本历史 ----

    private void RefreshVersions()
    {
        Versions.Clear();

        if (_currentMeta is null)
        {
            HasVersions = false;
            SelectedVersion = null;
            return;
        }

        foreach (var v in _currentMeta.Versions.OrderByDescending(v => v.Version))
        {
            Versions.Add(new VersionItemViewModel(v, v.Version == _currentMeta.CurrentVersion));
        }

        HasVersions = Versions.Count > 0;
        SelectedVersion = Versions.FirstOrDefault();
    }

    /// <summary>
    /// 回退。语义按方案第六节：把旧版本内容**复制成新版本**，历史一个都不删，
    /// 所以回退之后还能继续往前退，也能再退回到刚才那一版。
    /// </summary>
    private void RevertVersion()
    {
        if (_currentMeta is null || SelectedVersion is null)
        {
            return;
        }

        var target = SelectedVersion.Number;
        if (target == _currentMeta.CurrentVersion)
        {
            Warn($"v{target} 本来就是当前版本，不用回退。");
            return;
        }

        var next = _currentMeta.CurrentVersion + 1;
        var answer = MessageBox.Show(
            $"把 v{target} 的内容回退成新的一版 v{next}？\n\n"
            + "做法是复制而不是覆盖：现有版本文件一个都不会删，回退完还能再退回去。",
            "回退版本",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _currentMeta = _draftService.RevertToVersion(_currentMeta, target);
            var previous = _currentMeta.CurrentVersion - 1;

            var text = _draftService.LoadVersionText(_currentMeta.Title, _currentMeta.CurrentVersion);
            EditingText = text;
            OutputText = text;
            HasDraft = true;
            DraftLabel = $"{_currentMeta.Title} v{_currentMeta.CurrentVersion} · 回退自 v{target}";
            ResetRevision();
            RefreshVersions();
            RefreshDrafts();
            StatusMessage = $"已回退：v{target} 的内容复制成了 v{_currentMeta.CurrentVersion}，v1~v{previous} 全都还在。";
        }
        catch (Exception ex)
        {
            Warn("回退失败：" + ex.Message);
        }
    }

    /// <summary>左栏点击作品时打开它。</summary>
    public void OpenDraft(DraftItemViewModel? item)
    {
        if (item is null || item.Title == _currentMeta?.Title)
        {
            return;
        }

        var meta = _draftService.LoadMeta(item.Title);
        if (meta is null)
        {
            Warn($"读不到 {item.Title} 的 meta.json。");
            return;
        }

        _currentMeta = meta;
        _title = meta.Title;
        _titleTouched = true;
        OnPropertyChanged(nameof(Title));

        Outline = meta.Outline;

        if (!string.IsNullOrWhiteSpace(meta.Model))
        {
            if (!Models.Contains(meta.Model))
            {
                Models.Add(meta.Model);
            }

            SelectedModel = meta.Model;
        }

        var text = _draftService.LoadVersionText(meta.Title, meta.CurrentVersion);
        OutputText = text;
        EditingText = text;
        HasDraft = true;
        DraftLabel = $"{meta.Title} v{meta.CurrentVersion} · {(meta.Status == DraftStatus.Final ? "已入终稿" : "底稿")}";
        ResetRevision();
        RefreshVersions();

        _isLoading = true;
        foreach (var corpus in CorpusItems)
        {
            corpus.IsSelected = meta.CorpusFiles.Contains(corpus.File.FileName, StringComparer.OrdinalIgnoreCase);
        }

        _isLoading = false;
        SyncRecipe();

        StatusMessage = $"已打开 {meta.Title} v{meta.CurrentVersion}。";
    }

    /// <summary>范文卡片双击预览（设计图上的「点卡片弹整篇预览」）。</summary>
    public void PreviewCorpus(CorpusItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var text = _corpusService.ReadText(item.File);
            CorpusPreviewWindow.Show(item.File, text);
        }
        catch (Exception ex)
        {
            Warn("读不出这篇范文：" + ex.Message);
        }
    }

    private void SelectDraftInList(string title)
    {
        var target = DraftItems.FirstOrDefault(d => d.Title == title);
        if (target is not null)
        {
            SelectedDraft = target;
        }
    }

    private string SuggestTitleFromOutline()
    {
        var firstLine = (Outline ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? "";

        if (firstLine.Length > 18)
        {
            firstLine = firstLine[..18];
        }

        return string.IsNullOrWhiteSpace(firstLine) ? $"未命名 {DateTime.Now:MMdd-HHmm}" : firstLine;
    }

    private static void Warn(string message) =>
        MessageBox.Show(message, "InkHarbor", MessageBoxButton.OK, MessageBoxImage.Information);

    public void Dispose()
    {
        _generationCts?.Cancel();
        _generationCts?.Dispose();
        _reviseCts?.Cancel();
        _reviseCts?.Dispose();
        _suggestCts?.Cancel();
        _suggestCts?.Dispose();
        _embedding.Dispose();
        _ollama.Dispose();
    }
}
