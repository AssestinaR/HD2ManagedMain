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

    public GameDataIndexWindowViewModel(IReadOnlyList<GameDataArchiveSummary> archives, GameDataIndexFingerprint fingerprint, ModLibraryService library, ProfileService profiles, string gameDataDirectory, IAssetArchiveIndexService index)
    {
        this.index = index;
		var deployed = new DeployedPatchOverlayResolver().ResolveAsync(gameDataDirectory).AsTask().GetAwaiter().GetResult();
        var overlays = BuildOverlays(archives.Select(item => item.PackageName), library, profiles, deployed);
        Archives = new(archives.Select(archive => new GameDataArchiveRowViewModel(archive, overlays.GetValueOrDefault(archive.PackageName, ArchiveOverlay.None))));
        ArchivesView = CollectionViewSource.GetDefaultView(Archives);
        ArchivesView.Filter = FilterArchive;
        ArchivesView.SortDescriptions.Add(new(nameof(GameDataArchiveRowViewModel.Category), ListSortDirection.Ascending));
        ArchivesView.SortDescriptions.Add(new(nameof(GameDataArchiveRowViewModel.DisplayName), ListSortDirection.Ascending));
        CategoryFilters = new[] { "全部" }.Concat(Archives.Select(row => row.Category).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)).ToArray();
        Summary = $"Archive {fingerprint.ArchivesIndexed}/{fingerprint.ArchivesTotal}，AssetKey {fingerprint.AssetKeysTotal} · 已生效 {Archives.Count(row => row.ReplacementStatus == "已生效")} · 竞争 {Archives.Count(row => row.ReplacementStatus == "竞争生效")} · 异常 {Archives.Count(row => row.ReplacementStatus == "异常")}";
        BuiltText = $"构建时间：{fingerprint.BuiltUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        SourceText = $"来源：{fingerprint.GameDataDirectory}";
        ProfileText = $"当前配置：{profiles.ActiveKey ?? "未启用"}";
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

    private static Dictionary<string, ArchiveOverlay> BuildOverlays(IEnumerable<string> packageNames, ModLibraryService library, ProfileService profiles, DeployedPatchOverlay deployed)
    {
        var result = packageNames.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(name => name, _ => new ArchiveOverlay(), StringComparer.OrdinalIgnoreCase);
        var activeEntries = profiles.ActiveProfile is { } active ? profiles.GetSortedEntries(active).Where(entry => entry.Enabled).ToDictionary(entry => entry.NodeId) : new();
        foreach (var pair in library.DerivedData.Nodes)
        {
            if (pair.Value.AssetSummary is not { } summary) continue;
            foreach (var asset in summary.Assets)
            {
                var targets = asset.SemanticTargetArchiveIds.Count > 0 ? asset.SemanticTargetArchiveIds : new[] { asset.Key.ArchiveId };
                foreach (var target in targets)
                {
                    if (!result.TryGetValue(target, out var overlay)) continue;
                    overlay.LibraryMods.Add(summary.Name);
                    if (activeEntries.ContainsKey(pair.Key)) overlay.EnabledMods.Add(summary.Name);
                    overlay.AssetCandidates.Add(new ModAssetCandidate(pair.Key, summary.Name, asset.Key.AssetKey));
                }
            }
        }

        foreach (var overlay in result.Values)
        {
            foreach (var candidate in overlay.AssetCandidates)
            {
                var winners = deployed.Groups.Where(group => group.IsValid && group.AssetKeys.Contains(candidate.AssetKey)).OrderBy(group => group.TargetPatchIndex).ToArray();
                if (winners.Length == 0) continue;
                var winner = winners[^1];
                var winnerName = winner.NodeId is { } nodeId && library.DerivedData.Find(nodeId)?.AssetSummary is { } winnerSummary ? winnerSummary.Name : "未知 Mod";
                overlay.Effective.Add(new(winnerName, winner.PatchGroupName, winner.TargetPatchIndex, candidate.AssetKey, winners.Select(group => group.NodeId).Distinct().Count() > 1));
            }
            if (deployed.Issues.Any(issue => overlay.AssetCandidates.Any(candidate => candidate.NodeId == issue.NodeId))) overlay.HasError = true;
        }
        foreach (var overlay in result.Values) overlay.FinalizeStatus();
        return result;
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

    internal GameDataArchiveRowViewModel(GameDataArchiveSummary archive, ArchiveOverlay overlay)
    {
        PackageName = archive.PackageName; DisplayName = archive.DisplayName; Category = archive.Category; EntryCount = archive.EntryCount;
        ReplacementStatus = archive.Status == "存在问题" ? "异常" : overlay.Status;
        EffectivePatchGroup = archive.Status == "存在问题" ? "—" : overlay.EffectivePatchGroup;
        EffectiveMod = archive.Status == "存在问题" ? "—" : overlay.EffectiveMod;
        AssetCountBackground = Brushes.Transparent;
        AssetCountForeground = archive.Status == "存在问题" ? Color("#DC2626") : Color("#1F2937");
        ReplacementBackground = Brushes.Transparent;
        ReplacementForeground = ReplacementStatus switch { "Mod 库中存在" => Color("#2563EB"), "当前配置启用" => Color("#CA8A04"), "已生效" => Color("#166534"), "竞争生效" => Color("#4D7C0F"), "异常" => Color("#DC2626"), _ => Color("#6B7280") };
        EffectiveByAsset = overlay.Effective.GroupBy(item => item.AssetKey).ToDictionary(group => group.Key, group => group.OrderBy(item => item.LoadOrder).Last());
    }
    private static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
}

internal sealed class ArchiveOverlay
{
    public static ArchiveOverlay None => new();
    public HashSet<string> LibraryMods { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EnabledMods { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EffectivePatch> Effective { get; } = new();
	public List<ModAssetCandidate> AssetCandidates { get; } = new();
	public bool HasError { get; set; }
    public string Status { get; private set; } = "未替换";
    public string EffectivePatchGroup { get; private set; } = "—";
    public string EffectiveMod { get; private set; } = "—";
    public void FinalizeStatus()
    {
		if (HasError) { Status = "异常"; return; }
        if (Effective.Count > 0)
        {
            var winner = Effective.OrderBy(item => item.LoadOrder).Last();
            Status = Effective.Any(item => item.HasCompetition) ? "竞争生效" : "已生效";
            EffectivePatchGroup = Effective.Select(item => item.PatchGroup).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? winner.PatchGroup : $"{Effective.Select(item => item.PatchGroup).Distinct(StringComparer.OrdinalIgnoreCase).Count()} 个 patch 组";
            EffectiveMod = Effective.Select(item => item.ModName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? winner.ModName : $"{Effective.Select(item => item.ModName).Distinct(StringComparer.OrdinalIgnoreCase).Count()} 个 Mod";
        }
        else if (EnabledMods.Count > 0) { Status = "当前配置启用"; EffectiveMod = EnabledMods.Count == 1 ? EnabledMods.Single() : $"{EnabledMods.Count} 个 Mod"; }
        else if (LibraryMods.Count > 0) { Status = "Mod 库中存在"; EffectiveMod = LibraryMods.Count == 1 ? LibraryMods.Single() : $"{LibraryMods.Count} 个 Mod"; }
    }
}

internal sealed record EffectivePatch(string ModName, string PatchGroup, int LoadOrder, AssetKey AssetKey, bool HasCompetition);
internal sealed record ModAssetCandidate(ModNodeId NodeId, string ModName, AssetKey AssetKey);
