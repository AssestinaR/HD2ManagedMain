using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HD2ModCore.Domain;

namespace HD2ModManager.Views;

// Purpose: Displays and copies detailed AssetKey facts for one indexed Game Data archive.
public partial class GameDataArchiveDetailsWindow : Window
{
    public GameDataArchiveDetailsWindow() => InitializeComponent();

    private void OnCopyField(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string property } && DataContext is GameDataArchiveDetailsWindowViewModel viewModel)
            viewModel.CopyField(property);
    }

    private void OnAssetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: GameDataArchiveAssetRowViewModel row } && DataContext is GameDataArchiveDetailsWindowViewModel viewModel)
            viewModel.CopyAsset(row);
    }

    private void OnAssetsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && sender is ListView { SelectedItem: GameDataArchiveAssetRowViewModel row } && DataContext is GameDataArchiveDetailsWindowViewModel viewModel)
        {
            viewModel.CopyAsset(row);
            e.Handled = true;
        }
    }
}

public sealed class GameDataArchiveDetailsWindowViewModel : INotifyPropertyChanged
{
    private string copyStatus = string.Empty;
    private readonly DispatcherTimer clearTimer;
    public string DisplayName { get; }
    public string PackageName { get; }
    public string Category { get; }
    public string ReplacementStatus { get; }
    public string EffectivePatchGroup { get; }
    public string EffectiveMod { get; }
    public string ContentSummary { get; }
    public ObservableCollection<GameDataArchiveAssetRowViewModel> Assets { get; }
    public string CopyStatus { get => copyStatus; private set { copyStatus = value; PropertyChanged?.Invoke(this, new(nameof(CopyStatus))); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public GameDataArchiveDetailsWindowViewModel(GameDataArchiveDetails details, GameDataArchiveRowViewModel row)
    {
        DisplayName = row.DisplayName; PackageName = row.PackageName; Category = row.Category;
        ReplacementStatus = row.ReplacementStatus; EffectivePatchGroup = row.EffectivePatchGroup; EffectiveMod = row.EffectiveMod;
        Assets = new(details.Assets.Select(asset => new GameDataArchiveAssetRowViewModel(asset, row)));
        var groups = Assets.GroupBy(asset => NormalizeType(asset.TypeName)).ToDictionary(group => group.Key, group => group.Count());
        ContentSummary = $"Game Data 内容：Unit × {Count(groups, "Unit")} · Material × {Count(groups, "Material")} · Texture × {Count(groups, "Texture")} · Animation × {Count(groups, "Animation")} · 其他 × {Assets.Count - Count(groups, "Unit") - Count(groups, "Material") - Count(groups, "Texture") - Count(groups, "Animation")}";
        clearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        clearTimer.Tick += (_, _) => { clearTimer.Stop(); CopyStatus = string.Empty; };
    }

    public void CopyField(string property)
    {
        var value = property switch { nameof(DisplayName) => DisplayName, nameof(PackageName) => PackageName, nameof(Category) => Category, nameof(ReplacementStatus) => ReplacementStatus, nameof(EffectivePatchGroup) => EffectivePatchGroup, nameof(EffectiveMod) => EffectiveMod, _ => string.Empty };
        if (string.IsNullOrEmpty(value)) return;
        Clipboard.SetText(value); ShowCopied($"已复制 {property}");
    }

    public void CopyAsset(GameDataArchiveAssetRowViewModel row)
    {
        Clipboard.SetText($"Type={row.TypeName}\nTypeID={row.TypeId}\nFileID={row.FileId}\nPackage={PackageName}\nDisplayName={DisplayName}\nEffectivePatchGroup={row.EffectivePatchGroup}\nEffectiveMod={row.EffectiveMod}\nSharedPackages={string.Join(", ", row.SharedPackages)}\nSharedObjects={string.Join(", ", row.SharedObjects)}");
        ShowCopied("已复制 AssetKey 信息");
    }

    private void ShowCopied(string message) { CopyStatus = message; clearTimer.Stop(); clearTimer.Start(); }
    private static int Count(IReadOnlyDictionary<string, int> groups, string key) => groups.GetValueOrDefault(key);
    private static string NormalizeType(string type) { var value = type.ToLowerInvariant(); if (value.Contains("unit")) return "Unit"; if (value.Contains("material")) return "Material"; if (value.Contains("texture")) return "Texture"; if (value.Contains("animation") || value.Contains("state_machine")) return "Animation"; return "Other"; }
}

public sealed class GameDataArchiveAssetRowViewModel
{
    public string TypeName { get; }
    public string TypeId { get; }
    public string FileId { get; }
    public string Hash => $"{TypeId} / {FileId}";
    public string FriendlyName { get; }
    public string EffectivePatchGroup { get; }
    public string EffectiveMod { get; }
    public IReadOnlyList<string> SharedPackages { get; }
    public IReadOnlyList<string> SharedObjects { get; }
    public string SharedPackagesText => SharedPackages.Count == 0 ? "—" : SharedPackages.Count == 1 ? SharedPackages[0] : $"{SharedPackages.Count} 个 Package";
    public string SharedObjectsText => SharedObjects.Count == 0 ? "—" : SharedObjects.Count == 1 ? SharedObjects[0] : $"{SharedObjects.Count} 个对象";
    public string SharedPackagesTooltip => SharedPackages.Count == 0 ? "没有其他 Package 使用此 AssetKey" : string.Join(Environment.NewLine, SharedPackages);
    public string SharedObjectsTooltip => SharedObjects.Count == 0 ? "没有其他对象使用此 AssetKey" : string.Join(Environment.NewLine, SharedObjects);

    public GameDataArchiveAssetRowViewModel(GameDataArchiveAssetEntry asset, GameDataArchiveRowViewModel archive)
    {
        TypeName = asset.TypeName; TypeId = asset.AssetKey.TypeId.ToString("x16"); FileId = asset.AssetKey.FileId.ToString("x16"); FriendlyName = asset.FriendlyName;
        if (archive.EffectiveByAsset.TryGetValue(asset.AssetKey, out var effective)) { EffectivePatchGroup = effective.PatchGroup; EffectiveMod = effective.ModName; }
        else { EffectivePatchGroup = "—"; EffectiveMod = "—"; }
        SharedPackages = asset.SharedPackages; SharedObjects = asset.SharedDisplayNames;
    }
}
