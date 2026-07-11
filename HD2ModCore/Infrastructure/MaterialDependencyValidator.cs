using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：验证被 Unit 引用的 source material 及其 texture entries 已在 patch 中闭合。
// Purpose: Validates that source materials referenced by Units and their texture entries are closed in the patch.
public sealed class MaterialDependencyValidator : IMaterialDependencyValidator
{
	public const ulong MaterialTypeId = 0xeac0b497876adedf;
	public const ulong TextureTypeId = 0xcd4238c6a0c69e32;

	private readonly IPatchEntryPayloadReader payloadReader;
	private readonly StingrayMaterialReferenceReader materialReferenceReader;

	public MaterialDependencyValidator(IPatchEntryPayloadReader payloadReader, StingrayMaterialReferenceReader materialReferenceReader)
	{
		this.payloadReader = payloadReader ?? throw new ArgumentNullException(nameof(payloadReader));
		this.materialReferenceReader = materialReferenceReader ?? throw new ArgumentNullException(nameof(materialReferenceReader));
	}

	public async ValueTask<MaterialDependencyValidationResult> ValidateAsync(
		IReadOnlyCollection<ulong> materialIds,
		IReadOnlyList<PatchTocEntry> patchEntries,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(materialIds);
		ArgumentNullException.ThrowIfNull(patchEntries);
		var entryByKey = patchEntries.ToDictionary(entry => entry.AssetKey);
		var validMaterialIds = new HashSet<ulong>();
		var materialTextureIds = new Dictionary<ulong, IReadOnlyList<ulong>>();
		var rejectedReasons = new Dictionary<ulong, string>();

		foreach (var materialId in materialIds.Distinct())
		{
			var materialKey = new AssetKey(MaterialTypeId, materialId);
			if (!entryByKey.TryGetValue(materialKey, out var materialEntry))
			{
				rejectedReasons[materialId] = "Source material entry is missing from patch.";
				continue;
			}

			IReadOnlyList<ulong> textureIds;
			try
			{
				var payload = await payloadReader.ReadPayloadAsync(materialEntry, cancellationToken).ConfigureAwait(false);
				textureIds = materialReferenceReader.ReadTextureIds(payload.TocData);
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
			{
				rejectedReasons[materialId] = $"Source material texture references could not be read: {ex.Message}";
				continue;
			}

			materialTextureIds[materialId] = textureIds;
			var missingTextures = textureIds
				.Where(textureId => !entryByKey.ContainsKey(new AssetKey(TextureTypeId, textureId)))
				.Distinct()
				.ToArray();
			if (missingTextures.Length > 0)
			{
				rejectedReasons[materialId] = $"Missing texture entries: {string.Join(", ", missingTextures.Select(textureId => $"0x{textureId:x16}"))}.";
				continue;
			}

			validMaterialIds.Add(materialId);
		}

		return new MaterialDependencyValidationResult(validMaterialIds, materialTextureIds, rejectedReasons);
	}
}