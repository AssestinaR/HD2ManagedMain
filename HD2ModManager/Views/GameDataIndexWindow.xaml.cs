using HD2ModCore.Domain;
using HD2ModCore.Application;
using HD2ModCore.Infrastructure;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HD2ModManager.Services;

namespace HD2ModManager.Views;

// Purpose: Displays lightweight archive summaries from the persisted Core asset index.
public partial class GameDataIndexWindow : Window
{
    public GameDataIndexWindow() => InitializeComponent();

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader { Tag: string property } && DataContext is GameDataIndexWindowViewModel viewModel)
            viewModel.Sort(property);
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: GameDataArchiveRowViewModel row } && DataContext is GameDataIndexWindowViewModel viewModel)
            viewModel.OpenDetails(row, this);
    }
}

public sealed class GameDataIndexWindowViewModel : INotifyPropertyChanged
{
    private readonly IAssetArchiveIndexService index;
    private string searchText = string.Empty;
    private string statusFilter = "全部";
    private string categoryFilter = "全部";

    public ObservableCollection<GameDataArchiveRowViewModel> Archives { get; }
    public ICollectionView ArchivesView { get; }
    public IReadOnlyList<string> StatusFilters { get; } = new[] { "全部", "未替换", "Mod 库中存在", "当前配置启用", "已生效", "竞争生效", "异常" };
    public IReadOnlyList<string> CategoryFilters { get; }
    public string Summary { get; }
    public string BuiltText { get; }
    public string SourceText { get; }
    public string ProfileText { get; }

    public string SearchText { get => searchText; set { if (searchText == value) return; searchText = value; OnChanged(nameof(SearchText)); ArchivesView.Refresh(); } }
    public string StatusFilter { get => statusFilter; set { if (statusFilter == value) return; statusFilter = value; OnChanged(nameof(StatusFilter)); ArchivesView.Refresh(); } }
    public string CategoryFilter { get => categoryFilter; set { if (categoryFilter == value) return; categoryFilter = value; OnChanged(nameof(CategoryFilter)); ArchivesView.Refresh(); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public GameDataIndexWindowViewModel(GameDataArchiveBrowserSnapshot snapshot, string? activeProfileName, IAssetArchiveIndexService index)
    {
        this.index = index;
        Archives = new(snapshot.Archives.Select(item => new GameDataArchiveRowViewModel(item, snapshot.ModNames)));
        ArchivesView = CollectionViewSource.GetDefaultView(Archives);
        ArchivesView.Filter = FilterArchive;
        ArchivesView.SortDescriptions.Add(new(nameof(GameDataArchiveRowViewModel.Category), ListSortDirection.Ascending));
        ArchivesView.SortDescriptions.Add(new(nameof(GameDataArchiveRowViewModel.DisplayName), ListSortDirection.Ascending));
        CategoryFilters = new[] { "全部" }.Concat(Archives.Select(row => row.Category).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)).ToArray();
        Summary = $"Archive {snapshot.Fingerprint.ArchivesIndexed}/{snapshot.Fingerprint.ArchivesTotal}，AssetKey {snapshot.Fingerprint.AssetKeysTotal} · 已生效 {Archives.Count(row => row.ReplacementStatus == "已生效")} · 竞争 {Archives.Count(row => row.ReplacementStatus == "竞争生效")} · 异常 {Archives.Count(row => row.ReplacementStatus == "异常")}";
        BuiltText = $"构建时间：{snapshot.Fingerprint.BuiltUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        SourceText = $"来源：{snapshot.Fingerprint.GameDataDirectory}";
        ProfileText = $"当前配置：{activeProfileName ?? "未启用"}";
    }

    public async void OpenDetails(GameDataArchiveRowViewModel row, Window owner)
    {
        var details = await index.GetArchiveDetailsAsync(row.PackageName);
        if (details is null) return;
        new GameDataArchiveDetailsWindow { Owner = owner, DataContext = new GameDataArchiveDetailsWindowViewModel(details, row) }.ShowDialog();
    }

    public void Sort(string propertyName)
    {
        var current = ArchivesView.SortDescriptions.FirstOrDefault(item => item.PropertyName == propertyName);
        var direction = current.PropertyName == propertyName && current.Direction == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ArchivesView.SortDescriptions.Clear();
        ArchivesView.SortDescriptions.Add(new(propertyName, direction));
    }

    private bool FilterArchive(object value)
    {
        if (value is not GameDataArchiveRowViewModel row) return false;
        if (StatusFilter != "全部" && StatusFilter != row.ReplacementStatus) return false;
        if (CategoryFilter != "全部" && !string.Equals(CategoryFilter, row.Category, StringComparison.OrdinalIgnoreCase)) return false;
        return string.IsNullOrWhiteSpace(SearchText) || row.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || row.PackageName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || row.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || row.EffectiveMod.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class GameDataArchiveRowViewModel
{
    public string PackageName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public int EntryCount { get; }
    public string ReplacementStatus { get; }
    public string EffectivePatchGroup { get; }
    public string EffectiveMod { get; }
    public Brush AssetCountBackground { get; }
	public Brush AssetCountForeground { get; }
    public Brush ReplacementBackground { get; }
    public Brush ReplacementForeground { get; }
    internal IReadOnlyDictionary<AssetKey, EffectivePatch> EffectiveByAsset { get; }

    internal GameDataArchiveRowViewModel(GameDataArchiveBrowserItem item, IReadOnlyDictionary<ModNodeId, string> modNames)
    {
        var archive = item.Archive;
        var overlay = item.Overlay;
        PackageName = archive.PackageName; DisplayName = archive.DisplayName; Category = archive.Category; EntryCount = archive.EntryCount;
        ReplacementStatus = archive.Status == "存在问题" || overlay.Issues.Any(issue => issue.Severity == CoreIssueSeverity.Error) ? "异常"
            : overlay.HasEffectiveReplacement ? overlay.HasCompetition ? "竞争生效" : "已生效"
            : overlay.HasActiveReplacement ? "当前配置启用"
            : overlay.HasLibraryReplacement ? "Mod 库中存在" : "未替换";
        EffectivePatchGroup = overlay.EffectiveTargetPatchIndexes.Count == 0 ? "—" : overlay.EffectiveTargetPatchIndexes.Count == 1 ? $"目标 patch_{overlay.EffectiveTargetPatchIndexes[0]}" : $"{overlay.EffectiveTargetPatchIndexes.Count} 个 patch 组";
        var effectiveNames = overlay.EffectiveModIds.Select(id => modNames.GetValueOrDefault(id) ?? "未知 Mod").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var activeNames = overlay.ActiveModIds.Select(id => modNames.GetValueOrDefault(id) ?? "未知 Mod").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var libraryNames = overlay.LibraryModIds.Select(id => modNames.GetValueOrDefault(id) ?? "未知 Mod").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var names = effectiveNames.Count > 0 ? effectiveNames : activeNames.Count > 0 ? activeNames : libraryNames;
        EffectiveMod = names.Count == 0 ? "—" : names.Count == 1 ? names[0] : $"{names.Count} 个 Mod";
        AssetCountBackground = Brushes.Transparent;
        AssetCountForeground = archive.Status == "存在问题" ? Color("#DC2626") : Color("#1F2937");
        ReplacementBackground = Brushes.Transparent;
        ReplacementForeground = ReplacementStatus switch { "Mod 库中存在" => Color("#2563EB"), "当前配置启用" => Color("#CA8A04"), "已生效" => Color("#166534"), "竞争生效" => Color("#4D7C0F"), "异常" => Color("#DC2626"), _ => Color("#6B7280") };
        EffectiveByAsset = overlay.EffectiveAssets
            .GroupBy(asset => asset.AssetKey)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var winner = group.OrderBy(asset => asset.TargetPatchIndex).Last();
                    var modName = winner.WinnerNodeId is { } nodeId ? modNames.GetValueOrDefault(nodeId) ?? "未知 Mod" : "未知 Mod";
                    return new EffectivePatch(modName, $"目标 patch_{winner.TargetPatchIndex}", winner.TargetPatchIndex, winner.AssetKey, winner.HasCompetition);
                });
    }
    private static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
}

internal sealed record EffectivePatch(string ModName, string PatchGroup, int LoadOrder, AssetKey AssetKey, bool HasCompetition);
