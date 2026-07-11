namespace HD2ModAdaptation.PatchReconstruction;

// Purpose: Defines the minimal game archive access needed to resolve material dependency payloads.
public interface IGameDataPackageResolver
{
	ValueTask<GameDataPackageToc?> GetPackageTocAsync(string packageName, CancellationToken cancellationToken = default);

	ValueTask<byte[]?> GetPackageResourceAsync(string packageName, ulong resourceOffset, uint resourceSize, CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken cancellationToken = default);
}

public sealed record GameDataPackageToc(byte[] Data, bool UsesSlimEntryOffset);