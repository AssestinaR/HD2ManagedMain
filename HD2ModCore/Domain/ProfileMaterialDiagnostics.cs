namespace HD2ModCore.Domain;

// Purpose: Represents transient winner-first Unit, Material and Texture diagnostics for one profile revision.
public enum ProfileMaterialDiagnosticKind
{
	MissingMaterial,
	MissingTexture,
	NoEffectiveUnitConsumer,
	UnreachableResource
}

public sealed record ProfileMaterialDiagnostic(
	ModNodeId NodeId,
	AssetKey AssetKey,
	ProfileMaterialDiagnosticKind Kind,
	string Summary,
	string Detail,
	AssetKey? RelatedAssetKey = null);

public sealed record ProfileMaterialDiagnostics(
	ProfileId ProfileId,
	long ProfileRevision,
	DateTimeOffset BuiltUtc,
	IReadOnlyList<ProfileMaterialDiagnostic> Items,
	IReadOnlyList<CoreIssue> Issues);