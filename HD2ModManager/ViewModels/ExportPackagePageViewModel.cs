using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using HD2ModManager.Models;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels;

public sealed class ExportPackagePageViewModel : FullWorkspacePageViewModel
{
    private readonly ModLibraryService _library;
    private readonly NotificationService _notifications;
    private ExportPackageEntry? _awaitingEntry;
    private string _outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HD2ModManager Exports");
    private string _packageName = "新建 Mod 包";
    private string _state = "在左侧选择一个行来筛选目标";
    private string _candidateQuery = string.Empty;
    private readonly List<ExportCandidateItem> _candidateSource = [];

    public ExportPackagePageViewModel(ModLibraryService library, NotificationService notifications)
    {
        _library = library;
        _notifications = notifications;
        Title = "导出 Mod";
        Root = new ExportPackageEntry(0, "主要内容");
        RefreshRows();
        AddOptionCommand = new RelayCommand(_ => AddOption());
        AddSubOptionCommand = new RelayCommand(entry => AddSubOption(entry as ExportPackageEntry));
        RemoveEntryCommand = new RelayCommand(entry => RemoveEntry(entry as ExportPackageEntry));
        ChooseCommand = new RelayCommand(entry => BeginChoose(entry as ExportPackageEntry));
        ClearCommand = new RelayCommand(entry => Clear(entry as ExportPackageEntry));
        ExportCommand = new RelayCommand(async _ => await ExportAsync());
    }

    public ExportPackageEntry Root { get; }
    public ObservableCollection<ExportPackageEntry> Rows { get; } = [];
    public BulkObservableCollection<ExportCandidateItem> Candidates { get; } = new(item => item.SelectionKey);
    public RelayCommand AddOptionCommand { get; }
    public RelayCommand AddSubOptionCommand { get; }
    public RelayCommand RemoveEntryCommand { get; }
    public RelayCommand ChooseCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ExportCommand { get; }
    public string OutputDirectory { get => _outputDirectory; set => SetField(ref _outputDirectory, value); }
    public string PackageName { get => _packageName; set => SetField(ref _packageName, value); }
    public string State { get => _state; private set => SetField(ref _state, value); }
    public bool HasCandidates => Candidates.Count != 0;
    public string CandidatePlaceholder => _awaitingEntry is null ? "在左侧选择一个行来筛选目标" : "没有可作为此条目来源的 Mod";
    public string CandidateQuery
    {
        get => _candidateQuery;
        set
        {
            if (!SetField(ref _candidateQuery, value)) return;
            ApplyCandidateFilter(ListTransitionKind.Filter);
        }
    }
    public string CandidateSummary => _awaitingEntry is null ? "" : $"可选来源：{Candidates.Count} 个 Mod";

    public void AddOption()
    {
        Root.Children.Add(new ExportPackageEntry(1, "新选项") { Parent = Root });
        RefreshRows();
    }

    private void AddSubOption(ExportPackageEntry? parent)
    {
        if (parent is null || parent.Level != 1) return;
        parent.Children.Add(new ExportPackageEntry(2, "新子选项") { Parent = parent });
        RefreshRows();
    }

    private void RemoveEntry(ExportPackageEntry? entry)
    {
        if (entry?.Parent is null) return;
        entry.Parent.Children.Remove(entry);
        if (ReferenceEquals(_awaitingEntry, entry)) ExitChoose();
        RefreshRows();
    }

    private void BeginChoose(ExportPackageEntry? entry)
    {
        if (entry is null) return;
        _awaitingEntry = entry;
        _candidateQuery = string.Empty;
        OnPropertyChanged(nameof(CandidateQuery));
        _candidateSource.Clear();
        _candidateSource.AddRange(_library.All()
            .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .Select(mod => new ExportCandidateItem(mod)));
        ApplyCandidateFilter(ListTransitionKind.Filter);
        State = Candidates.Count == 0 ? "没有可作为此条目来源的 Mod" : $"正在为“{entry.Name}”选择来源 Mod";
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(CandidatePlaceholder));
        OnPropertyChanged(nameof(CandidateSummary));
        RefreshRows();
    }

    public void SelectCandidate(ExportCandidateItem? candidate)
    {
        if (_awaitingEntry is null || candidate is null) return;
        _awaitingEntry.ModId = candidate.Mod.Guid;
        _awaitingEntry.Name = candidate.Mod.Name;
        _awaitingEntry.Notes = candidate.Mod.Description ?? string.Empty;
        _awaitingEntry.ImagePath = candidate.Mod.Image;
        ExitChoose();
        RefreshRows();
    }

    private void Clear(ExportPackageEntry? entry)
    {
        if (entry is null) return;
        entry.ModId = null;
        entry.ImagePath = null;
        if (ReferenceEquals(_awaitingEntry, entry)) ExitChoose();
        RefreshRows();
    }

    private void ExitChoose()
    {
        _awaitingEntry = null;
        ApplyCandidateFilter(ListTransitionKind.Filter);
        State = "在左侧选择一个行来筛选目标";
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(CandidatePlaceholder));
        OnPropertyChanged(nameof(CandidateSummary));
    }

    private void ApplyCandidateFilter(ListTransitionKind transitionKind)
    {
        var query = _candidateQuery.Trim();
        var visible = _awaitingEntry is null
            ? Enumerable.Empty<ExportCandidateItem>()
            : _candidateSource.Where(candidate =>
                (_awaitingEntry.Level != 0 || !candidate.IsDecoration)
                && (query.Length == 0
                    || candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (candidate.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)));

        Candidates.ReplaceWith(visible, transitionKind);
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(CandidatePlaceholder));
        OnPropertyChanged(nameof(CandidateSummary));
    }

    private void RefreshRows()
    {
        Rows.Clear();
        AddRows(Root);
    }

    private void AddRows(ExportPackageEntry entry)
    {
        entry.IsAwaitingSelection = ReferenceEquals(entry, _awaitingEntry);
        Rows.Add(entry);
        foreach (var child in entry.Children) AddRows(child);
    }

    private async Task ExportAsync()
    {
        try
        {
            State = "正在生成标准 Mod 包...";
            var path = await new StandardModPackageExportService(_library).ExportAsync(Root, PackageName, OutputDirectory);
            State = "导出完成：" + path;
            _notifications.Show("Mod 包已导出。", NotificationLevel.Info, TimeSpan.FromSeconds(8));
        }
        catch (Exception exception)
        {
            State = "导出失败：" + exception.Message;
            _notifications.Show(State, NotificationLevel.Error, TimeSpan.FromSeconds(10));
        }
    }
}

public sealed class ExportPackageEntry(int level, string name) : BaseViewModel
{
    private string _name = name;
    private string _notes = string.Empty;
    private string? _modId;
    private string? _imagePath;
    private bool _isAwaitingSelection;
    public int Level { get; } = level;
    public double Indent => Level * 22d;
    public ExportPackageEntry? Parent { get; init; }
    public ObservableCollection<ExportPackageEntry> Children { get; } = [];
    public string Name { get => _name; set => SetField(ref _name, value); }
    public string Notes { get => _notes; set => SetField(ref _notes, value); }
    public string? ModId { get => _modId; set { if (SetField(ref _modId, value)) OnPropertyChanged(nameof(ChooseLabel)); } }
    public string? ImagePath { get => _imagePath; set => SetField(ref _imagePath, value); }
    public bool IsAwaitingSelection { get => _isAwaitingSelection; set => SetField(ref _isAwaitingSelection, value); }
    public bool IsRoot => Level == 0;
    public bool CanAddSubOption => Level == 1;
    public string KindText => Level == 0 ? "Root" : Level == 1 ? "选项" : "子选项";
    public string ChooseLabel => string.IsNullOrWhiteSpace(ModId) ? "选择" : "清除";
}

public sealed class ExportCandidateItem(ModEntity mod) : BaseViewModel, IModListSelectable
{
    public ModEntity Mod { get; } = mod;
    public string Name => Mod.Name;
    public string? Description => Mod.Description;
    public string? ImagePath => Mod.Image;
    public string SelectionKey => Mod.Guid;
    public bool IsDecoration => Mod.IsDecoration;
    public string AssetSummaryText => string.Empty;
    public string UserStatusTitle => string.Empty;
    public bool IsModelOutdated => false;
    public bool HasRowActions => false;
    private bool _isVisible = true;
    private bool _isSelected;
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
}
