using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModManager.Models;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels;

// Purpose: Captures a reusable decoration attachment plan before the Canonical append engine is available.
public sealed class DecorationPlanPageViewModel : PageViewModel
{
    private readonly ModLibraryService _library;
    private readonly NotificationService _notifications;
    private readonly IEquipmentUnitCatalogService _equipmentCatalog;
    private readonly IModFileResolver _modFileResolver;
    private string _targetBodyVariant = "双身形";
    private string _dualVariantMode = "自动从来源分配";
    private string _targetPart = "LeftArm";
    private string _outputDirectory;
    private bool _autoImport = true;
    private bool _showPotentialCulling;
    private string _targetQuery = string.Empty;
    private IReadOnlyList<DecorationSourceUnitItem> _allSourceUnits = Array.Empty<DecorationSourceUnitItem>();
    private IReadOnlyList<DecorationTargetModItem> _allTargetMods = Array.Empty<DecorationTargetModItem>();
    private IReadOnlyDictionary<string, IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry>> _preparedSourceEntries = new Dictionary<string, IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry>>(StringComparer.OrdinalIgnoreCase);
    private string _state = "正在读取来源 Unit。";

    public DecorationPlanPageViewModel(ModLibraryService library, NotificationService notifications, string sourceModId)
    {
        _library = library;
        _notifications = notifications;
        _equipmentCatalog = CoreServices.CreateEquipmentUnitCatalogService(SettingsService.CreateStoragePaths());
        _modFileResolver = CoreServices.CreateModFileResolver();
        SourceModId = sourceModId;
        SourceName = library.Get(sourceModId)?.Name ?? sourceModId;
        _outputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
        BodyVariants = ["双身形", "仅健壮", "仅纤细"];
        DualVariantModes = ["自动从来源分配", "来源全部附加到每一个身形"];
        TargetParts = ["Head", "Torso", "Hips", "LeftArm", "RightArm", "LeftLeg", "RightLeg"];
        GenerateCommand = new RelayCommand(async _ => await GenerateAsync());
        BrowseOutputCommand = new RelayCommand(BrowseOutput);
        ToggleTargetSearchCommand = new RelayCommand(() =>
        {
            ShowTargetSearch = !ShowTargetSearch;
            OnPropertyChanged(nameof(ShowTargetSearch));
        });
        Title = "生成装饰 Mod";
        _ = LoadAsync();
    }

    public string SourceModId { get; }
    public string SourceName { get; }
    public ObservableCollection<DecorationSourceUnitItem> SourceUnits { get; } = new();
    public BulkObservableCollection<DecorationTargetModItem> TargetMods { get; } = new(item => item.SelectionKey);
    public IReadOnlyList<string> BodyVariants { get; }
    public IReadOnlyList<string> DualVariantModes { get; }
    public IReadOnlyList<string> TargetParts { get; }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand ToggleTargetSearchCommand { get; }
    public bool CanGenerate => SourceUnits.Any(item => item.IsSelected) && _allTargetMods.Any(item => item.IsSelected);
    public string State { get => _state; private set => SetField(ref _state, value); }
    public string TargetBodyVariant { get => _targetBodyVariant; set => SetField(ref _targetBodyVariant, value); }
    public string DualVariantMode { get => _dualVariantMode; set => SetField(ref _dualVariantMode, value); }
    public string TargetPart { get => _targetPart; set => SetField(ref _targetPart, value); }
    public string OutputDirectory { get => _outputDirectory; set => SetField(ref _outputDirectory, value); }
    public bool AutoImport { get => _autoImport; set => SetField(ref _autoImport, value); }
    public bool ShowPotentialCulling
    {
        get => _showPotentialCulling;
        set
        {
            if (!SetField(ref _showPotentialCulling, value)) return;
            RefreshSourceUnits();
        }
    }
    public bool ShowTargetSearch { get; private set; }
    public string TargetQuery
    {
        get => _targetQuery;
        set
        {
            if (!SetField(ref _targetQuery, value)) return;
            RefreshTargetVisibility();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var source = _library.Snapshot.Nodes.Values.FirstOrDefault(node => node.Id.Value.ToString("N") == SourceModId);
            if (source is null) { State = "来源 Mod 已不存在。"; return; }
            var result = await _library.InformationCenter.RequestAssetInventoryAsync(source, _library.ModsRootDirectory,
                new ModInformationRequest(ModInformationKind.AssetInventory, "DecorationPlan"));
            if (result.Data is not null)
            {
                var unitKeys = result.Data.PatchGroups.SelectMany(group => group.AssetKeys)
                    .Where(key => key.TypeId == PatchUnitMeshReader.UnitTypeId)
                    .Distinct()
                    .ToHashSet();
                var catalogEntries = await _equipmentCatalog.GetEntriesAsync(unitKeys);
                var patchPaths = await _modFileResolver.ResolvePatchFilesAsync(source, _library.ModsRootDirectory);
                var workspaceReader = new HD2ModAdaptation.PatchReconstruction.PatchWorkspace.PatchWorkspaceReader();
                var workspaces = await Task.WhenAll(patchPaths.Select(path => workspaceReader.ReadIndexAsync(path).AsTask()));
                _preparedSourceEntries = workspaces.ToDictionary(
                    workspace => workspace.SourcePatchTocPath,
                    workspace => workspace.Entries,
                    StringComparer.OrdinalIgnoreCase);
                var transferable = await _equipmentCatalog.FilterTransferableSourcePartsAsync(catalogEntries, patchPaths, cancellationToken: default, preparedEntries: _preparedSourceEntries);
                _allSourceUnits = transferable
                    .SelectMany(entry => entry.Parts)
                    .GroupBy(part => new { part.UnitAssetKey, part.MeshInfoIndex, part.PartKind, part.BodyVariant, part.Layer })
                    .Select(group => new DecorationSourceUnitItem(group.First(), OnSelectionChanged))
                    .OrderBy(item => item.PartKind)
                    .ThenBy(item => item.BodyVariant)
                    .ThenBy(item => item.Layer)
                    .ThenBy(item => item.FileId)
                    .ToArray();
                RefreshSourceUnits();
            }
            _allTargetMods = _library.All()
                .Where(mod => !mod.IsDecoration && !string.Equals(mod.Guid, SourceModId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(mod => mod.Name)
                .Select(mod => new DecorationTargetModItem(mod.Guid, mod.Name, mod.Description, mod.Image, OnSelectionChanged))
                .ToArray();
            RefreshTargetVisibility();
            State = SourceUnits.Count == 0 ? "来源中没有可识别的 Unit。" : $"已读取 {SourceUnits.Count} 个 Unit；选择后将写入装饰计划。";
        }
        catch (Exception exception)
        {
            State = $"读取来源失败：{exception.Message}";
        }
    }

    private void RefreshSourceUnits()
    {
        SourceUnits.Clear();
        foreach (var item in _allSourceUnits.Where(item => ShowPotentialCulling || !item.IsCulling)) SourceUnits.Add(item);
        OnSelectionChanged();
    }

    private void RefreshTargetVisibility()
    {
        var query = TargetQuery.Trim();
        var visible = _allTargetMods.Where(item =>
            query.Length == 0
            || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (item.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true));
        TargetMods.ReplaceWith(visible, ListTransitionKind.Filter);
    }

    public void ApplyTargetSelection(IReadOnlyList<string> selectedKeys)
    {
        var selected = selectedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _allTargetMods) item.IsSelected = selected.Contains(item.ModId);
        // ModListPanel may synchronize several rows in one request. The page owns
        // the command state, so do not rely on individual row setters to refresh it.
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.RaiseCanExecuteChanged();
    }

    private void BrowseOutput()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = OutputDirectory };
        if (dialog.ShowDialog() == true) OutputDirectory = dialog.FolderName;
    }

    private async Task GenerateAsync()
    {
        var sourceUnits = SourceUnits.Where(item => item.IsSelected).Select(item => new DecorationSourceUnit
        {
            TypeId = item.TypeId,
            FileId = item.FileId,
            MeshInfoIndex = item.MeshInfoIndex,
            BodyVariant = item.BodyVariant.ToString(),
            Layer = item.Layer.ToString(),
            IsCulling = item.IsCulling
        }).ToList();
        var targetModGuids = _allTargetMods.Where(item => item.IsSelected).Select(item => item.ModId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sourceUnits.Count == 0 || targetModGuids.Count == 0)
        {
            State = sourceUnits.Count == 0 && targetModGuids.Count == 0
                ? "请选择来源 Unit 和目标 Mod。"
                : sourceUnits.Count == 0 ? "请选择至少一个来源 Unit。" : "请选择至少一个目标 Mod。";
            _notifications.Show(State, NotificationLevel.Info, TimeSpan.FromSeconds(8));
            return;
        }
        var plan = new DecorationPlanDocument
        {
            Plan = new DecorationAttachmentPlan
            {
                TargetPart = TargetPart,
                TargetBodyVariant = NormalizeBodyVariant(TargetBodyVariant),
                DualVariantMode = NormalizeDualVariantMode(DualVariantMode),
                TargetModGuids = targetModGuids
            }
        };
        var displayName = $"{SourceName} - 装饰";
        try
        {
            if (AutoImport)
            {
                var created = await _library.CreateDecorationAsync(SourceModId, sourceUnits, plan, displayName, _preparedSourceEntries);
                var libraryDirectory = _library.ResolveAbsolutePath(created.SourcePath);
                var outputDirectory = Path.Combine(OutputDirectory, SanitizeFileName(displayName));
                if (!string.IsNullOrWhiteSpace(libraryDirectory)) CopyDecorationFiles(libraryDirectory, outputDirectory);
                State = "装饰计划已导入模组库。";
                _notifications.Show($"已生成装饰 Mod：{created.Name}");
                if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel shell) shell.OpenModDetails(created.Guid);
                return;
            }

            Directory.CreateDirectory(OutputDirectory);
            var path = Path.Combine(OutputDirectory, SanitizeFileName(displayName));
            var source = _library.Snapshot.Nodes.Values.Single(node => node.Id.Value.ToString("N") == SourceModId);
            plan.Name = displayName;
            plan.Payloads = (await new DecorationPayloadCompiler(_modFileResolver)
                .CompileAsync(source, _library.ModsRootDirectory, sourceUnits, plan.Plan, path, _preparedSourceEntries)).ToList();
            await File.WriteAllTextAsync(Path.Combine(path, "decoration.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            State = $"计划已写出：{path}";
            _notifications.Show("装饰计划已写入输出目录。");
        }
        catch (Exception exception)
        {
            State = $"生成失败：{exception.Message}";
            LogService.Error($"装饰 Mod 生成失败：来源={SourceModId}，错误={exception}");
            _notifications.Show(State, NotificationLevel.Error, TimeSpan.FromSeconds(10));
        }
    }

    private static string SanitizeFileName(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static string NormalizeBodyVariant(string value)
        => value.Contains("健") ? "Stocky" : value.Contains("纤") ? "Slim" : "Dual";

    private static void CopyDecorationFiles(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var fileName in new[] { "decoration.json", "stocky.bin", "slim.bin" })
        {
            var source = Path.Combine(sourceDirectory, fileName);
            var destination = Path.Combine(destinationDirectory, fileName);
            if (File.Exists(source)) File.Copy(source, destination, overwrite: true);
            else if (File.Exists(destination)) File.Delete(destination);
        }
    }

    private static string NormalizeDualVariantMode(string value)
        => value.Contains("全部") ? "ApplyAllToBoth" : "AutoAssign";
}

public sealed class DecorationSourceUnitItem : BaseViewModel
{
    private readonly Action _changed;
    private bool _isSelected;
    public DecorationSourceUnitItem(EquipmentUnitPart part, Action changed)
    {
        Part = part;
        TypeId = part.UnitAssetKey.TypeId;
        FileId = part.UnitAssetKey.FileId;
        _changed = changed;
    }
    public EquipmentUnitPart Part { get; }
    public ulong TypeId { get; }
    public ulong FileId { get; }
    public int MeshInfoIndex => Part.MeshInfoIndex;
    public string Label => $"0x{FileId:x16}";
    public UnitMeshPartKind PartKind => Part.PartKind;
    public UnitMeshBodyVariant BodyVariant => Part.BodyVariant;
    public UnitMeshPartLayer Layer => Part.Layer;
    public bool IsCulling => Part.IsCullingMesh || Part.Layer == UnitMeshPartLayer.Culling;
    public string StoredBitsText => Part.StoredBytes <= 0 ? "未知" : $"{Part.StoredBytes * 8:N0} bit";
    public bool IsSelected { get => _isSelected; set { if (SetField(ref _isSelected, value)) _changed(); } }
}

public sealed class DecorationTargetModItem : BaseViewModel, IModListSelectable
{
    private readonly Action _changed;
    private bool _isSelected;
    private bool _isVisible = true;
    public DecorationTargetModItem(string modId, string name, string? description, string? image, Action changed)
    {
        ModId = modId;
        Name = name;
        Description = description;
        Image = image;
        _changed = changed;
    }
    public string ModId { get; }
    public string SelectionKey => ModId;
    public bool IsDecoration => false;
    public string Name { get; }
    public string? Description { get; }
    public string? Image { get; }
    public string? ImagePath => Image;
    public string AssetSummaryText => "";
    public bool IsModelOutdated => false;
    public string UserStatusTitle => "候选 Mod";
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }
    public bool IsSelected { get => _isSelected; set { if (SetField(ref _isSelected, value)) _changed(); } }
}
