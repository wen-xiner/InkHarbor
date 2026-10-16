using System.Collections.ObjectModel;
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

    private CancellationTokenSource? _generationCts;
    private bool _isLoading;
    private bool _titleTouched;

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

        RefreshCommand = new RelayCommand(_ => _ = RefreshAllAsync());
        RemoveChipCommand = new RelayCommand(p => RemoveChip(p as ChipViewModel));
        SavePresetCommand = new RelayCommand(_ => SavePreset());
        GenerateCommand = new RelayCommand(_ => _ = GenerateAsync(), _ => !IsGenerating);
        CancelGenerateCommand = new RelayCommand(_ => _generationCts?.Cancel());
        SaveVersionCommand = new RelayCommand(_ => SaveVersion());
        ConfirmFinalCommand = new RelayCommand(_ => ConfirmFinal());
        DiscardCommand = new RelayCommand(_ => DiscardChanges());
        ToggleEditPanelCommand = new RelayCommand(_ => IsEditPanelOpen = !IsEditPanelOpen);

        _ = RefreshAllAsync();
    }

    // ---- 集合 ----

    public ObservableCollection<CorpusItemViewModel> CorpusItems { get; } = new();

    public ObservableCollection<DraftItemViewModel> DraftItems { get; } = new();

    public ObservableCollection<ChipViewModel> Recipe { get; } = new();

    public ObservableCollection<string> Models { get; } = new();

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
            }
        }
    }

    public bool IsIdle => !IsGenerating;

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
        private set => SetProperty(ref _hasDraft, value);
    }

    public DraftItemViewModel? SelectedDraft
    {
        get => _selectedDraft;
        set => SetProperty(ref _selectedDraft, value);
    }

    // ---- 刷新 ----

    private async Task RefreshAllAsync()
    {
        _isLoading = true;
        try
        {
            _workspace.EnsureDirectories();

            var selectedBefore = Recipe.Select(c => c.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            CorpusItems.Clear();
            foreach (var file in _corpusService.Scan())
            {
                var vm = new CorpusItemViewModel(file, OnCorpusToggled);
                CorpusItems.Add(vm);
                vm.IsSelected = selectedBefore.Contains(file.FileName);
            }

            SyncRecipe();
            RefreshDrafts();

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
        StatusMessage = $"预设「{name.Trim()}」已存进 presets\\。";
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
        StatusMessage = $"已丢掉本轮未保存的改动，回到磁盘上的 v{_currentMeta.CurrentVersion}（已存的版本不删）。";
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
        _ollama.Dispose();
    }
}
