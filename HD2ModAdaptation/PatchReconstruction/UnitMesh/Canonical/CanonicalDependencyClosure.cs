using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Audits the first-version canonical target material closure without writing a Patch or consulting Manager state.
// SDK reference: SaveMeshMaterials() collects material slots after Entry.Save(); GetEntryByLoadArchive() resolves the
// original Game Data entry with IgnorePatch=True; GetEntry() resolves the active/session view; AddEntryToPatchID()
// copies an explicitly selected entry into the active Patch. This C# audit intentionally keeps those operations read-only:
// target Unit material bindings are read from the rebuilt Unit, Material/Texture payloads are read from Game Data or
// explicit canonical session entries, and no source dependency set is copied implicitly.
public sealed class CanonicalDependencyClosure
{
	private readonly IPatchTocScanner tocScanner;
	private readonly UnitMaterialReferenceReader unitMaterialReader;
	private readonly StingrayMaterialReferenceReader materialReferenceReader;
	private readonly Func<string, IGameDataPackageResolver> gameResolverFactory;

	public CanonicalDependencyClosure(
		IPatchTocScanner? tocScanner = null,
		UnitMaterialReferenceReader? unitMaterialReader = null,
		StingrayMaterialReferenceReader? materialReferenceReader = null,
		Func<string, IGameDataPackageResolver>? gameResolverFactory = null)
	{
		this.tocScanner = tocScanner ?? new PatchTocScanner();
		this.unitMaterialReader = unitMaterialReader ?? new UnitMaterialReferenceReader();
		this.materialReferenceReader = materialReferenceReader ?? new StingrayMaterialReferenceReader();
		this.gameResolverFactory = gameResolverFactory ?? (directory => new GameDataPackageResolver(directory));
	}

	public async ValueTask<CanonicalDependencyClosureResult> ValidateAsync(
		CanonicalDependencyClosureRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var diagnostics = new List<CanonicalDependencyDiagnostic>();
		var dependencies = new List<CanonicalDependencyAsset>();
		var missing = new List<CanonicalDependencyAsset>();
		var unknown = new List<CanonicalDependencyDiagnostic>();
		var session = request.SessionEntries.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.Single());
		IGameDataPackageResolver? game = string.IsNullOrWhiteSpace(request.GameDataDirectory) ? null : gameResolverFactory(request.GameDataDirectory);

		IReadOnlyList<UnitMaterialReferenceBinding> bindings;
		try { bindings = unitMaterialReader.ReadReferenceBindings(request.TargetUnitTocData); }
		catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or OverflowException)
		{
			diagnostics.Add(new("UnknownTargetMaterialGraph", $"目标 Unit 的 Material bindings 无法解析：{exception.Message}"));
			return new(false, Array.Empty<CanonicalDependencyAsset>(), Array.Empty<CanonicalDependencyAsset>(), diagnostics, diagnostics);
		}

		foreach (var materialId in bindings.Select(binding => binding.MaterialId).Distinct().Order())
		{
			var materialKey = new AssetKey(MaterialDependencyResolver.MaterialTypeId, materialId);
			var material = await ResolveAsync(materialKey, session, game, cancellationToken).ConfigureAwait(false);
			if (material is null)
			{
				missing.Add(new(materialKey, CanonicalDependencyOrigin.Unknown, "Material entry was not found in Game Data or the canonical Patch session."));
				diagnostics.Add(new("MissingMaterial", $"目标 Unit 引用的 Material {Format(materialKey)} 不存在。"));
				continue;
			}

			dependencies.Add(new(materialKey, material.Origin, material.Name));
			IReadOnlyList<ulong> textureIds;
			try { textureIds = materialReferenceReader.ReadTextureIds(material.TocData); }
			catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or OverflowException)
			{
				var diagnostic = new CanonicalDependencyDiagnostic("UnknownTextureGraph", $"Material {Format(materialKey)} 的 Texture graph 无法由现有 Stingray reader 解析：{exception.Message}");
				unknown.Add(diagnostic);
				diagnostics.Add(diagnostic);
				continue;
			}

			// SDK-compatible semantics: zero denotes an intentionally empty texture slot.
			foreach (var textureId in textureIds.Where(id => id != 0).Distinct().Order())
			{
				var textureKey = new AssetKey(MaterialDependencyResolver.TextureTypeId, textureId);
				var texture = await ResolveAsync(textureKey, session, game, cancellationToken).ConfigureAwait(false);
				if (texture is null)
				{
					missing.Add(new(textureKey, CanonicalDependencyOrigin.Unknown, $"Texture referenced by Material {Format(materialKey)} was not found."));
					diagnostics.Add(new("MissingTexture", $"Material {Format(materialKey)} 引用的 Texture {Format(textureKey)} 不存在。"));
					continue;
				}
				dependencies.Add(new(textureKey, texture.Origin, texture.Name));
			}
		}

		return new(missing.Count == 0 && unknown.Count == 0, dependencies, missing, diagnostics, unknown);
	}

	private async ValueTask<ResolvedEntry?> ResolveAsync(AssetKey key, IReadOnlyDictionary<AssetKey, CanonicalPatchSessionEntry> session, IGameDataPackageResolver? game, CancellationToken cancellationToken)
	{
		if (session.TryGetValue(key, out var sessionEntry))
			return new(key, CanonicalDependencyOrigin.PatchSession, "Explicit canonical Patch session entry", sessionEntry.EffectiveTocData);
		if (game is null) return null;
		foreach (var packageName in await game.GetPackageNamesAsync(cancellationToken).ConfigureAwait(false))
		{
			GameDataPackageToc? toc;
			try { toc = await game.GetPackageTocAsync(packageName, cancellationToken).ConfigureAwait(false); }
			catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException) { continue; }
			if (toc is null) continue;
			IReadOnlyList<PatchTocEntry> entries;
			try { entries = tocScanner.ScanEntries(toc.Data, Path.GetFileName(packageName), toc.UsesSlimEntryOffset); }
			catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException) { continue; }
			var entry = entries.FirstOrDefault(candidate => candidate.AssetKey == key);
			if (entry is null) continue;
			var payload = await game.GetPackageResourceAsync(packageName, entry.TocDataOffset, entry.TocDataSize, cancellationToken).ConfigureAwait(false);
			if (payload is null || payload.Length < entry.TocDataSize) continue;
			return new(key, CanonicalDependencyOrigin.GameData, Path.GetFileName(packageName), payload.Length == entry.TocDataSize ? payload : payload.AsSpan(0, checked((int)entry.TocDataSize)).ToArray());
		}
		return null;
	}

	private static string Format(AssetKey key) => $"0x{key.TypeId:x16}/0x{key.FileId:x16}";
	private sealed record ResolvedEntry(AssetKey Key, CanonicalDependencyOrigin Origin, string Name, byte[] TocData);
}

public sealed record CanonicalDependencyClosureRequest(AssetKey TargetUnitKey, byte[] TargetUnitTocData, IReadOnlyCollection<CanonicalPatchSessionEntry> SessionEntries, string? GameDataDirectory = null);
public enum CanonicalDependencyOrigin { Unknown = 0, GameData = 1, PatchSession = 2 }
public sealed record CanonicalDependencyAsset(AssetKey AssetKey, CanonicalDependencyOrigin Origin, string Detail);
public sealed record CanonicalDependencyDiagnostic(string Code, string Message);
public sealed record CanonicalDependencyClosureResult(bool IsValid, IReadOnlyList<CanonicalDependencyAsset> Dependencies, IReadOnlyList<CanonicalDependencyAsset> Missing, IReadOnlyList<CanonicalDependencyDiagnostic> Diagnostics, IReadOnlyList<CanonicalDependencyDiagnostic> Unknown)
{
	public CanonicalDependencyClosureValidation Validation => IsValid ? CanonicalDependencyClosureValidation.Valid : CanonicalDependencyClosureValidation.Invalid;
}
