namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Reads one explicitly selected vanilla Unit and its explicitly allowed dependencies.
public sealed class GameDataUnitMeshReader
{
	private readonly IGameDataPackageResolver resolver;
	private readonly IPatchTocScanner tocScanner;
	private readonly UnitMeshReader unitMeshReader;
	private readonly Dictionary<string, IReadOnlyList<PatchTocEntry>> entriesByArchive = new(StringComparer.OrdinalIgnoreCase);

	public GameDataUnitMeshReader(
		IGameDataPackageResolver resolver,
		IPatchTocScanner? tocScanner = null,
		UnitMeshReader? unitMeshReader = null)
	{
		this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
		this.tocScanner = tocScanner ?? new PatchTocScanner();
		this.unitMeshReader = unitMeshReader ?? new UnitMeshReader();
	}

	public void PrimeEntries(IReadOnlyDictionary<string, IReadOnlyList<PatchTocEntry>> knownEntries)
	{
		ArgumentNullException.ThrowIfNull(knownEntries);
		foreach (var (archiveName, entries) in knownEntries)
		{
			entriesByArchive.TryAdd(archiveName, entries);
		}
	}

	// Purpose: Releases archive reconstruction caches at an explicit phase boundary.
	public void ClearCaches()
	{
		entriesByArchive.Clear();
		if (resolver is GameDataPackageResolver packageResolver)
			packageResolver.ClearCaches();
	}

	public async ValueTask<GameDataUnitMesh> ReadAsync(
		string archiveName,
		AssetKey unitAssetKey,
		IReadOnlyCollection<string>? dependencyArchiveNames = null,
		bool allowGlobalDependencySearch = false,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
		if (unitAssetKey.TypeId != PatchUnitMeshReader.UnitTypeId)
		{
			throw new InvalidDataException($"Asset type 0x{unitAssetKey.TypeId:x16} is not a Unit resource.");
		}

		var archives = CreateArchiveScope(archiveName, dependencyArchiveNames);
		var scopedEntries = new Dictionary<string, IReadOnlyList<PatchTocEntry>>(StringComparer.OrdinalIgnoreCase);
		foreach (var scopedArchiveName in archives)
		{
			scopedEntries[scopedArchiveName] = await GetEntriesAsync(scopedArchiveName, cancellationToken).ConfigureAwait(false);
		}

		var unitEntry = FindEntry(scopedEntries[archiveName], unitAssetKey, archiveName);
		var unitPayload = await ReadPayloadAsync(archiveName, unitEntry, cancellationToken).ConfigureAwait(false);
		var compositePayload = await ReadReferencedPayloadAsync(
			scopedEntries,
			unitPayload,
			16,
			PatchUnitMeshReader.CompositeUnitTypeId,
			"Composite",
			isRequired: true,
			allowGlobalDependencySearch,
			cancellationToken).ConfigureAwait(false);
		var bonePayload = await ReadReferencedPayloadAsync(
			scopedEntries,
			unitPayload,
			8,
			PatchUnitMeshReader.BoneTypeId,
			"bone",
			isRequired: false,
			allowGlobalDependencySearch,
			cancellationToken).ConfigureAwait(false);
		var boneNames = bonePayload is null ? UnitBoneNames.Empty : new UnitBoneNamesReader().Read(bonePayload.TocData);
		var model = compositePayload is null
			? unitMeshReader.Read(unitPayload.TocData, unitPayload.GpuResourceData, boneNames: boneNames)
			: unitMeshReader.Read(unitPayload.TocData, unitPayload.GpuResourceData, compositePayload.TocData, compositePayload.GpuResourceData, boneNames);
		return new GameDataUnitMesh(unitAssetKey, archiveName, unitPayload, model, compositePayload);
	}

	private async ValueTask<IReadOnlyList<PatchTocEntry>> GetEntriesAsync(string archiveName, CancellationToken cancellationToken)
	{
		if (entriesByArchive.TryGetValue(archiveName, out var cached))
		{
			return cached;
		}

		var toc = await resolver.GetPackageTocAsync(archiveName, cancellationToken).ConfigureAwait(false)
			?? throw new FileNotFoundException($"Could not resolve archive TOC '{archiveName}'.", archiveName);
		var entries = tocScanner.ScanEntries(toc.Data, archiveName, toc.UsesSlimEntryOffset);
		entriesByArchive[archiveName] = entries;
		return entries;
	}

	private static IReadOnlyList<string> CreateArchiveScope(string archiveName, IReadOnlyCollection<string>? dependencyArchiveNames)
	{
		var archives = new List<string> { archiveName };
		if (dependencyArchiveNames is not null)
		{
			foreach (var dependencyArchiveName in dependencyArchiveNames)
			{
				ArgumentException.ThrowIfNullOrWhiteSpace(dependencyArchiveName);
				if (!archives.Contains(dependencyArchiveName, StringComparer.OrdinalIgnoreCase))
				{
					archives.Add(dependencyArchiveName);
				}
			}
		}

		return archives;
	}

	private async ValueTask<PatchEntryPayload?> ReadReferencedPayloadAsync(
		IDictionary<string, IReadOnlyList<PatchTocEntry>> entriesByArchive,
		PatchEntryPayload unitPayload,
		int referenceOffset,
		ulong typeId,
		string resourceName,
		bool isRequired,
		bool allowGlobalDependencySearch,
		CancellationToken cancellationToken)
	{
		if (unitPayload.TocData.Length < referenceOffset + sizeof(ulong))
		{
			throw new InvalidDataException($"Unit TocData is too short to read its {resourceName} reference.");
		}

		var fileId = BitConverter.ToUInt64(unitPayload.TocData, referenceOffset);
		if (fileId == 0)
		{
			return null;
		}

		foreach (var (archiveName, entries) in entriesByArchive)
		{
			var entry = entries.SingleOrDefault(candidate => candidate.AssetKey == new AssetKey(typeId, fileId));
			if (entry is not null)
			{
				return await ReadPayloadAsync(archiveName, entry, cancellationToken).ConfigureAwait(false);
			}
		}

		if (!allowGlobalDependencySearch)
		{
			if (isRequired)
			{
				throw new InvalidDataException($"Unit references {resourceName} asset 0x{fileId:x16}, but it is absent from the explicit archive scope.");
			}
			return null;
		}

		// Armor Units often keep Composite/Bones in a different archive. Search lazily and cache
		// only the TOCs actually required by this Unit instead of preloading the whole Game Data set.
		foreach (var archiveName in await resolver.GetPackageNamesAsync(cancellationToken).ConfigureAwait(false))
		{
			if (archiveName.EndsWith(".stream", StringComparison.OrdinalIgnoreCase)
				|| archiveName.EndsWith(".gpu_resources", StringComparison.OrdinalIgnoreCase)
				|| entriesByArchive.ContainsKey(archiveName))
			{
				continue;
			}

			IReadOnlyList<PatchTocEntry> entries;
			try
			{
				entries = await GetEntriesAsync(archiveName, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
			{
				continue;
			}
			entriesByArchive[archiveName] = entries;
			var entry = entries.SingleOrDefault(candidate => candidate.AssetKey == new AssetKey(typeId, fileId));
			if (entry is not null)
			{
				return await ReadPayloadAsync(archiveName, entry, cancellationToken).ConfigureAwait(false);
			}
		}

		if (isRequired)
		{
			throw new InvalidDataException($"Unit references {resourceName} asset 0x{fileId:x16}, but it is absent from the explicit archive scope.");
		}

		return null;
	}

	private async ValueTask<PatchEntryPayload> ReadPayloadAsync(string archiveName, PatchTocEntry entry, CancellationToken cancellationToken)
	{
		var tocData = await ReadRequiredResourceAsync(archiveName, entry.TocDataOffset, entry.TocDataSize, "TOC", cancellationToken).ConfigureAwait(false);
		var streamData = await ReadOptionalResourceAsync(archiveName + ".stream", entry.StreamOffset, entry.StreamSize, cancellationToken).ConfigureAwait(false);
		var gpuData = await ReadOptionalResourceAsync(archiveName + ".gpu_resources", entry.GpuResourceOffset, entry.GpuResourceSize, cancellationToken).ConfigureAwait(false);
		return new PatchEntryPayload(entry, tocData, streamData, gpuData);
	}

	private async ValueTask<byte[]> ReadRequiredResourceAsync(string archiveName, ulong offset, uint size, string label, CancellationToken cancellationToken)
	{
		if (size == 0)
		{
			throw new InvalidDataException($"Archive entry has an empty {label} payload.");
		}

		return await ReadResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask<byte[]> ReadOptionalResourceAsync(string archiveName, ulong offset, uint size, CancellationToken cancellationToken)
		=> size == 0 ? Array.Empty<byte>() : await ReadResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);

	private async ValueTask<byte[]> ReadResourceAsync(string archiveName, ulong offset, uint size, CancellationToken cancellationToken)
	{
		var data = await resolver.GetPackageResourceAsync(archiveName, offset, size, cancellationToken).ConfigureAwait(false);
		if (data is null || data.Length < size)
		{
			throw new EndOfStreamException($"Could not read payload at offset {offset} size {size} from archive '{archiveName}'.");
		}

		return data.Length == size ? data : data.AsSpan(0, checked((int)size)).ToArray();
	}

	private static PatchTocEntry FindEntry(IReadOnlyList<PatchTocEntry> entries, AssetKey assetKey, string archiveName)
		=> entries.SingleOrDefault(entry => entry.AssetKey == assetKey)
			?? throw new KeyNotFoundException($"Asset 0x{assetKey.TypeId:x16}/0x{assetKey.FileId:x16} was not found in archive '{archiveName}'.");
}