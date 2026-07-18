using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Encodings.Web;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

// Purpose: Presents a no-write source-to-target equipment transfer preview before cross-armor Patch output is implemented.
public sealed class CrossArmorTransferPlanWindowViewModel : INotifyPropertyChanged
{
	private EquipmentUnitCatalogEntry? selectedSource;
	private UnitMeshBodyVariant? selectedSourceBodyVariant;
	private CrossArmorBodyVariantPreference bodyVariantPreference = CrossArmorBodyVariantPreference.Slim;
	private CrossArmorLayerPreference layerPreference = CrossArmorLayerPreference.Armor;
	private bool allowManualMappings;
	private CrossArmorTransferPlan? plan;
	private readonly Dictionary<CrossArmorPhysicalTargetKey, CrossArmorManualMapping> manualMappings = new();
	private readonly HashSet<CrossArmorPhysicalTargetKey> suppressedTargets = new();
	private bool candidateGenerationRunning;

	public event PropertyChangedEventHandler? PropertyChanged;
	public IReadOnlyList<CrossArmorTransferEquipmentRow> SourceChoices { get; }
	public IReadOnlyList<CrossArmorTransferEquipmentRow> TargetChoices { get; }
	public string SourcePatchTocPath { get; }
	public string GameDataDirectory { get; }
	public RelayCommand RefreshPlanCommand { get; }
	public CrossArmorBodyVariantPreference BodyVariantPreference
	{
		get => bodyVariantPreference;
		set { if (bodyVariantPreference == value) return; bodyVariantPreference = value; OnPropertyChanged(); RefreshPlan(); }
	}
	public CrossArmorLayerPreference LayerPreference
	{
		get => layerPreference;
		set { if (layerPreference == value) return; layerPreference = value; OnPropertyChanged(); RefreshPlan(); }
	}
	public bool AllowManualMappings
	{
		get => allowManualMappings;
		set
		{
			if (allowManualMappings == value) return;
			allowManualMappings = value;
			if (!value)
			{
				manualMappings.Clear();
				suppressedTargets.Clear();
			}
			OnPropertyChanged();
			RefreshPlan();
		}
	}
	public EquipmentUnitCatalogEntry? SelectedSource
	{
		get => selectedSource;
		set
		{
			if (ReferenceEquals(selectedSource, value)) return;
			selectedSource = value;
			OnPropertyChanged();
			RefreshPlan();
		}
	}
	public UnitMeshBodyVariant? SelectedSourceBodyVariant
	{
		get => selectedSourceBodyVariant;
		set
		{
			if (selectedSourceBodyVariant == value) return;
			selectedSourceBodyVariant = value;
			OnPropertyChanged();
			RefreshPlan();
		}
	}
	public IReadOnlyList<EquipmentUnitPart> SourceParts => plan?.SelectedSource?.Parts
		.Where(part => SelectedSourceBodyVariant is null or UnitMeshBodyVariant.Unknown or UnitMeshBodyVariant.Any || part.BodyVariant == SelectedSourceBodyVariant || part.BodyVariant == UnitMeshBodyVariant.Any)
		.OrderBy(part => part.PartKind).ThenBy(part => part.Layer).ThenBy(part => part.MeshInfoIndex).ToArray()
		?? Array.Empty<EquipmentUnitPart>();
	public IReadOnlyList<CrossArmorTransferMapping> TargetMappings => plan?.Mappings
		.OrderBy(mapping => mapping.Target.PartKind)
		.ThenByDescending(mapping => mapping.Target.StoredBytes)
		.ThenBy(mapping => mapping.Target.Layer)
		.ThenBy(mapping => mapping.PhysicalTarget.UnitAssetKey.FileId)
		.ThenBy(mapping => mapping.PhysicalTarget.MeshInfoIndex)
		.ToArray()
		?? Array.Empty<CrossArmorTransferMapping>();
	public IReadOnlyList<CrossArmorTransferImpact> Impacts => plan?.Impacts ?? Array.Empty<CrossArmorTransferImpact>();
	public bool CanGenerateCandidate => plan?.CanContinue == true && !candidateGenerationRunning;
	public bool CandidateGenerationRunning
	{
		get => candidateGenerationRunning;
		set { if (candidateGenerationRunning == value) return; candidateGenerationRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerateCandidate)); }
	}
	public string Summary => plan is null
		? "请选择来源装备和至少一个目标装备。"
		: $"来源目录候选 {SourceParts.Count}；物理目标 mesh {TargetMappings.Count}（已按 Unit + mesh 去重）；预计替换 {TargetMappings.Count(mapping => mapping.WillReplace)}；预计极小化 {TargetMappings.Count(mapping => !mapping.WillReplace)}；受影响装备 {Impacts.Select(impact => impact.ArchiveId).Distinct().Count()}。";
	public string Issues => plan is null ? string.Empty : string.Join(Environment.NewLine, plan.Issues.Select(issue => $"{issue.Severity}: {issue.Message}"));

	private readonly IEquipmentUnitCatalogService catalogService;
	private readonly IReadOnlyList<EquipmentUnitCatalogEntry> sourceCandidates;
	private readonly IReadOnlyList<EquipmentUnitCatalogEntry> targetCandidates;

	public CrossArmorTransferPlanWindowViewModel(
		IEquipmentUnitCatalogService catalogService,
		IReadOnlyList<EquipmentUnitCatalogEntry> sourceCandidates,
		IReadOnlyList<EquipmentUnitCatalogEntry> targetCandidates,
		string sourcePatchTocPath,
		string gameDataDirectory)
	{
		this.catalogService = catalogService;
		this.sourceCandidates = sourceCandidates;
		this.targetCandidates = targetCandidates;
		SourcePatchTocPath = sourcePatchTocPath;
		GameDataDirectory = gameDataDirectory;
		SourceChoices = sourceCandidates.Select(entry => new CrossArmorTransferEquipmentRow(entry)).ToArray();
		TargetChoices = targetCandidates.Select(entry => new CrossArmorTransferEquipmentRow(entry)).ToArray();
		RefreshPlanCommand = new RelayCommand(RefreshPlan);
		SelectedSource = sourceCandidates.FirstOrDefault();
	}

	public void ExportJson(string outputPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
		RefreshPlan();
		var export = new
		{
			format = "hd2-cross-armor-transfer-plan-v1",
			exportedAtUtc = DateTimeOffset.UtcNow,
			isReadOnlyPlan = true,
			selectedSourceArchiveId = SelectedSource?.ArchiveId,
			selectedSourceBodyVariant = SelectedSourceBodyVariant?.ToString() ?? "Any",
			bodyVariantPreference = BodyVariantPreference.ToString(),
			layerPreference = LayerPreference.ToString(),
			manualSuppressions = suppressedTargets.Select(target => new { unitAssetKey = ToAssetKeyExport(target.UnitAssetKey), target.MeshInfoIndex }).ToArray(),
			selectedTargetArchiveIds = TargetChoices.Where(row => row.IsSelected).Select(row => row.Entry.ArchiveId).ToArray(),
			sourceChoices = SourceChoices.Select(row => ToChoiceExport(row)).ToArray(),
			targetChoices = TargetChoices.Select(row => ToChoiceExport(row)).ToArray(),
			plan = plan is null ? null : new
			{
				selectedSource = plan.SelectedSource is null ? null : ToEquipmentExport(plan.SelectedSource),
				selectedTargets = plan.SelectedTargets.Select(ToEquipmentExport).ToArray(),
				mappings = plan.Mappings.Select(mapping => new
				{
					physicalTarget = new { unitAssetKey = ToAssetKeyExport(mapping.PhysicalTarget.UnitAssetKey), mapping.PhysicalTarget.MeshInfoIndex },
					target = ToPartExport(mapping.Target),
					source = mapping.Source is null ? null : ToPartExport(mapping.Source),
					mapping.WillReplace,
					mapping.IsManual,
					mapping.IsSuppressed,
					mapping.UsedByArchiveIds,
					mapping.UsedByDisplayNames,
					mapping.Reason
				}).ToArray(),
				impacts = plan.Impacts.Select(impact => new
				{
					impact.ArchiveId,
					impact.DisplayName,
					sharedUnitAssetKey = ToAssetKeyExport(impact.SharedUnitAssetKey),
					partKind = impact.PartKind.ToString(),
					layer = impact.Layer.ToString()
				}).ToArray(),
				issues = plan.Issues.Select(issue => new { severity = issue.Severity.ToString(), issue.Code, issue.Message }).ToArray()
			}
		};
		File.WriteAllText(outputPath, JsonSerializer.Serialize(export, new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		}));
	}

	public void SetManualMapping(CrossArmorTransferMapping target, EquipmentUnitPart source)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(source);
		suppressedTargets.Remove(target.PhysicalTarget);
		manualMappings[target.PhysicalTarget] = new CrossArmorManualMapping(target.PhysicalTarget, source.UnitAssetKey, source.MeshInfoIndex);
		RefreshPlan();
	}

	public void SuppressAutomaticMapping(CrossArmorTransferMapping target)
	{
		ArgumentNullException.ThrowIfNull(target);
		manualMappings.Remove(target.PhysicalTarget);
		suppressedTargets.Add(target.PhysicalTarget);
		RefreshPlan();
	}

	public void ClearManualMapping(CrossArmorTransferMapping target)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (manualMappings.Remove(target.PhysicalTarget)) RefreshPlan();
	}

	public void RestoreAutomaticMapping(CrossArmorTransferMapping target)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (suppressedTargets.Remove(target.PhysicalTarget)) RefreshPlan();
	}

	private void RefreshPlan()
	{
		var targetIds = TargetChoices.Where(row => row.IsSelected).Select(row => row.Entry.ArchiveId).ToArray();
		plan = catalogService.CreatePlanAsync(sourceCandidates, targetCandidates, SelectedSource?.ArchiveId, SelectedSourceBodyVariant, BodyVariantPreference, LayerPreference, targetIds, manualMappings.Values.ToArray(), suppressedTargets.Select(target => new CrossArmorManualSuppression(target)).ToArray()).AsTask().GetAwaiter().GetResult();
		OnPropertyChanged(nameof(SourceParts));
		OnPropertyChanged(nameof(TargetMappings));
		OnPropertyChanged(nameof(Impacts));
		OnPropertyChanged(nameof(Summary));
		OnPropertyChanged(nameof(Issues));
		OnPropertyChanged(nameof(CanGenerateCandidate));
	}

	public CrossArmorTransferPlan? GetCurrentPlan()
	{
		RefreshPlan();
		return plan;
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	private static object ToEquipmentExport(EquipmentUnitCatalogEntry entry) => new
	{
		entry.ArchiveId,
		entry.Category,
		entry.DisplayName,
		parts = entry.Parts.Select(ToPartExport).ToArray()
	};

	private static object ToChoiceExport(CrossArmorTransferEquipmentRow row) => new
	{
		row.Entry.ArchiveId,
		row.Entry.Category,
		row.Entry.DisplayName,
		partCount = row.Entry.Parts.Count,
		row.IsSelected
	};

	private static object ToPartExport(EquipmentUnitPart part) => new
	{
		unitAssetKey = ToAssetKeyExport(part.UnitAssetKey),
		part.MeshInfoIndex,
		meshId = $"0x{part.MeshId:x8}",
		partKind = part.PartKind.ToString(),
		layer = part.Layer.ToString(),
		bodyVariant = part.BodyVariant.ToString(),
		part.SemanticName,
		part.Confidence,
		part.SharedArchiveIds
	};

	private static object ToAssetKeyExport(AssetKey key) => new
	{
		typeId = $"0x{key.TypeId:x16}",
		fileId = $"0x{key.FileId:x16}"
	};
}

// Purpose: Provides one selectable Armor or Helmet archive for the no-write transfer preview.
public sealed class CrossArmorTransferEquipmentRow : INotifyPropertyChanged
{
	private bool isSelected;
	public event PropertyChangedEventHandler? PropertyChanged;
	public EquipmentUnitCatalogEntry Entry { get; }
	public string Display => $"{Entry.DisplayName}（{Entry.Category}，{Entry.Parts.Count} 个可见部件）";
	public bool IsSelected
	{
		get => isSelected;
		set
		{
			if (isSelected == value) return;
			isSelected = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
		}
	}
	public CrossArmorTransferEquipmentRow(EquipmentUnitCatalogEntry entry) => Entry = entry;
}

// Purpose: Formats one Unit mesh fact for the simplified source list.
public sealed class CrossArmorSourcePartRow
{
	public CrossArmorSourcePartRow(EquipmentUnitPart part) => Part = part;
	public EquipmentUnitPart Part { get; }
	public string PartText => $"{PartName(Part.PartKind)} / {LayerName(Part.Layer)}";
	public string UnitText => $"0x{Part.UnitAssetKey.FileId:x16}";
	public string Detail => $"mesh {Part.MeshInfoIndex}，{VariantName(Part.BodyVariant)}，{Part.SemanticName}";
	private static string PartName(UnitMeshPartKind kind) => kind.ToString();
	private static string LayerName(UnitMeshPartLayer layer) => layer.ToString();
	private static string VariantName(UnitMeshBodyVariant variant) => variant.ToString();
}
