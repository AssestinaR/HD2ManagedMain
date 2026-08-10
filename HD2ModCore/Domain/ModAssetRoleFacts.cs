namespace HD2ModCore.Domain;

// Purpose: States how a Mod participates in Game Data replacement and library dependency relationships.
public enum ModAssetRole
{
	Unknown,
	Independent,
	GameDataOverride,
	DependencyComponent,
	GameDataOverrideAndDependencyProvider,
}

public sealed record ModAssetRoleFacts(
	ModNodeId NodeId,
	string Generation,
	DateTimeOffset BuiltUtc,
	ModAssetRole Role,
	int ProvidedAssetCount,
	int GameDataMappedAssetCount,
	int ExternalDependencyCount,
	int IncomingConsumerCount,
	IReadOnlyList<CoreIssue> Issues);
