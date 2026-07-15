using HD2ModCore.Domain;

namespace HD2ModManager.ViewModels;

// Purpose: Presents one immutable Mod AssetKey row for the advanced details table.
public sealed class AdvancedModAssetRowViewModel
{
	private readonly AdvancedModAssetRow row;

	public AdvancedModAssetRowViewModel(AdvancedModAssetRow row) => this.row = row;
	public AssetKey AssetKey => row.AssetKey;
	public string TypeName => row.TypeName;
	public string ResourceName => row.ResourceName;
	public string TargetSummary => row.TargetSummary;
	public string ReferenceSummary => row.ReferenceSummary;
	public string ProviderSummary => row.ProviderSummary;
	public string ProfileStatus => row.ProfileStatus;
	public string DiagnosticSummary => row.DiagnosticSummary;
	public string PatchGroupSummary => row.PatchGroupSummary;
	public long TocBytes => row.TocBytes;
	public long StreamBytes => row.StreamBytes;
	public long GpuBytes => row.GpuBytes;
	public string AssetKeyText => $"0x{AssetKey.TypeId:x16} / 0x{AssetKey.FileId:x16}";
	public bool HasIssue => !string.IsNullOrWhiteSpace(DiagnosticSummary);
}