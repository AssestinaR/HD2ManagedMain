using System.Buffers.Binary;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies canonical target-material closure auditing, explicit origins, and fail-closed diagnostics.
public sealed class CanonicalDependencyClosureTests
{
	private static readonly AssetKey Unit = new(PatchUnitMeshReader.UnitTypeId, 1);
	private static readonly AssetKey Material = new(MaterialDependencyResolver.MaterialTypeId, 2);
	private static readonly AssetKey Texture = new(MaterialDependencyResolver.TextureTypeId, 3);

	[Fact]
	public async Task ValidateAsync_AcceptsValidTargetMaterialClosureFromExplicitSession()
	{
		var result = await ValidateAsync(CreateMaterialPayload(Texture.FileId), CreateTextureEntry());

		Assert.True(result.IsValid);
		Assert.Empty(result.Missing);
		Assert.Equal(new[] { Material, Texture }, result.Dependencies.Select(dependency => dependency.AssetKey));
		Assert.All(result.Dependencies, dependency => Assert.Equal(CanonicalDependencyOrigin.PatchSession, dependency.Origin));
	}

	[Fact]
	public async Task ValidateAsync_RejectsMissingMaterial()
	{
		var result = await ValidateAsync(CreateUnitPayload(Material.FileId), includeMaterial: false);

		Assert.False(result.IsValid);
		Assert.Contains(result.Missing, dependency => dependency.AssetKey == Material);
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MissingMaterial");
	}

	[Fact]
	public async Task ValidateAsync_BlocksUnknownTextureGraphWhenReaderCannotParseMaterial()
	{
		var result = await ValidateAsync([1, 2, 3], false, new CanonicalPatchSessionEntry(Material, CanonicalPatchEntryOwnership.RequiredDependency, [1, 2, 3], [], []));

		Assert.False(result.IsValid);
		Assert.Contains(result.Unknown, diagnostic => diagnostic.Code == "UnknownTextureGraph");
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("Stingray reader", StringComparison.Ordinal));
	}

	private static async Task<CanonicalDependencyClosureResult> ValidateAsync(byte[] materialPayload, params CanonicalPatchSessionEntry[] additionalEntries)
		=> await ValidateAsync(materialPayload, includeMaterial: true, additionalEntries: additionalEntries);

	private static async Task<CanonicalDependencyClosureResult> ValidateAsync(byte[] materialPayload, bool includeMaterial, params CanonicalPatchSessionEntry[] additionalEntries)
	{
		var entries = new List<CanonicalPatchSessionEntry>();
		if (includeMaterial) entries.Add(new(Material, CanonicalPatchEntryOwnership.RequiredDependency, materialPayload, [], []));
		entries.AddRange(additionalEntries);
		return await new CanonicalDependencyClosure().ValidateAsync(new(Unit, CreateUnitPayload(Material.FileId), entries));
	}

	private static CanonicalPatchSessionEntry CreateTextureEntry()
		=> new(Texture, CanonicalPatchEntryOwnership.RequiredDependency, [4, 5], [], []);

	private static byte[] CreateUnitPayload(ulong materialId)
	{
		var data = new byte[0x84];
		Write32(data, 0x70, 0x74);
		Write32(data, 0x74, 1);
		Write32(data, 0x78, 7);
		Write64(data, 0x7c, materialId);
		return data;
	}

	private static byte[] CreateMaterialPayload(ulong textureId)
	{
		var data = new byte[148];
		Write32(data, 64, 1);
		Write64(data, 140, textureId);
		return data;
	}

	private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
	private static void Write64(byte[] data, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
}
