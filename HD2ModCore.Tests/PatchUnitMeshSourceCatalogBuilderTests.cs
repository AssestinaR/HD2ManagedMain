using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 source Unit mesh catalog builder 能从 patch 文件夹解析可选 source mesh 并记录失败项。
// Purpose: Verifies the source Unit mesh catalog builder parses selectable source meshes from patch directories and records failures.
public sealed class PatchUnitMeshSourceCatalogBuilderTests
{
	[Fact]
	public async Task BuildCatalogAsync_UnitEntries_BuildsMeshSummariesAndFailures()
	{
		var root = CreateTempDirectory();
		try
		{
			var patch0 = Path.Combine(root, "sample.patch_0");
			File.WriteAllBytes(patch0, []);
			var goodEntry = CreateEntry(patch0, entryIndex: 0, fileId: 0x1000);
			var emptyEntry = CreateEntry(patch0, entryIndex: 1, fileId: 0x2000);
			var failedEntry = CreateEntry(patch0, entryIndex: 2, fileId: 0x3000);
			var nonUnitEntry = new PatchTocEntry(new AssetKey(0x1234, 0x4000), patch0, Path.GetFileName(patch0), EntryIndex: 3);
			var reader = new FakePatchUnitMeshReader(new Dictionary<PatchTocEntry, PatchUnitMesh>
			{
				[goodEntry] = CreatePatchUnitMesh(goodEntry, hasRawMesh: true),
				[emptyEntry] = CreatePatchUnitMesh(emptyEntry, hasRawMesh: false),
			}, failedEntry);
			var builder = new PatchUnitMeshSourceCatalogBuilder(
				new PatchTocFileCollector(),
				new FakePatchTocScanner([goodEntry, emptyEntry, failedEntry, nonUnitEntry]),
				reader);

			var catalog = await builder.BuildCatalogAsync(root);

			Assert.Equal(Path.GetFullPath(root), catalog.PatchDirectoryPath);
			Assert.Equal([patch0], catalog.PatchTocFilePaths);
			Assert.Equal(1, catalog.PatchCount);
			var catalogEntry = Assert.Single(catalog.Entries);
			Assert.Equal(goodEntry, catalogEntry.Entry);
			Assert.Equal(1, catalogEntry.MeshCount);
			var mesh = Assert.Single(catalogEntry.Meshes);
			Assert.Equal(7, mesh.MeshInfoIndex);
			Assert.Equal(0xabcdu, mesh.MeshId);
			Assert.Equal(0, mesh.LodIndex);
			Assert.Equal(1u, mesh.StreamIndex);
			Assert.Equal(2u, mesh.VertexCount);
			Assert.Equal(3u, mesh.IndexCount);
			Assert.Equal(2u, mesh.MaterialCount);
			Assert.Equal(1u, mesh.SectionCount);
			Assert.Equal(16u, mesh.VertexStride);
			var component = Assert.Single(mesh.ComponentLayout);
			Assert.Equal(11u, component.Type);
			Assert.Equal(22u, component.Format);
			Assert.Equal(0u, component.Index);
			Assert.Equal(16u, component.Size);
			Assert.Equal(2, catalog.FailureCount);
			Assert.Contains(catalog.Failures, failure => failure.Entry == emptyEntry && failure.Exception is null && failure.IsUnsupportedUnitMeshFormat);
			Assert.Contains(catalog.Failures, failure => failure.Entry == failedEntry && failure.Exception is InvalidDataException && !failure.IsUnsupportedUnitMeshFormat);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public async Task BuildCatalogAsync_NoBufferStreamFailure_MarksUnsupportedUnitMeshFormat()
	{
		var root = CreateTempDirectory();
		try
		{
			var patch0 = Path.Combine(root, "sample.patch_0");
			File.WriteAllBytes(patch0, []);
			var entry = CreateEntry(patch0, entryIndex: 0, fileId: 0x1000);
			var reader = new FakePatchUnitMeshReader(
				new Dictionary<PatchTocEntry, PatchUnitMesh>(),
				entry,
				"Unsupported Unit mesh format: StreamInfoOffset is zero and no CompositeRef is present.");
			var builder = new PatchUnitMeshSourceCatalogBuilder(
				new PatchTocFileCollector(),
				new FakePatchTocScanner([entry]),
				reader);

			var catalog = await builder.BuildCatalogAsync(root);

			Assert.Empty(catalog.Entries);
			var failure = Assert.Single(catalog.Failures);
			Assert.Equal(entry, failure.Entry);
			Assert.True(failure.IsUnsupportedUnitMeshFormat);
			Assert.IsType<InvalidDataException>(failure.Exception);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public async Task BuildCatalogAsync_ScanFailure_RecordsPatchFailure()
	{
		var root = CreateTempDirectory();
		try
		{
			var patch0 = Path.Combine(root, "sample.patch_0");
			File.WriteAllBytes(patch0, []);
			var builder = new PatchUnitMeshSourceCatalogBuilder(
				new PatchTocFileCollector(),
				new ThrowingPatchTocScanner(),
				new FakePatchUnitMeshReader(new Dictionary<PatchTocEntry, PatchUnitMesh>()));

			var catalog = await builder.BuildCatalogAsync(root);

			Assert.Empty(catalog.Entries);
			var failure = Assert.Single(catalog.Failures);
			Assert.Equal(patch0, failure.Entry.SourceFilePath);
			Assert.IsType<InvalidDataException>(failure.Exception);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static PatchTocEntry CreateEntry(string patchPath, uint entryIndex, ulong fileId)
		=> new(
			new AssetKey(PatchUnitMeshReader.UnitTypeId, fileId),
			patchPath,
			Path.GetFileName(patchPath),
			TocDataSize: 1,
			EntryIndex: entryIndex);

	private static PatchUnitMesh CreatePatchUnitMesh(PatchTocEntry entry, bool hasRawMesh)
	{
		var streams = hasRawMesh
			? new[]
			{
				new UnitStreamInfo(
					Index: 1,
					Offset: 0,
					ComponentInfoId: 0,
					NumComponents: 1,
					VertexBufferId: 0,
					NumVertices: 2,
					VertexStride: 16,
					IndexBufferId: 0,
					NumIndices: 3,
					IndexBufferType: 0,
					VertexBufferOffset: 0,
					VertexBufferSize: 32,
					IndexBufferOffset: 32,
					IndexBufferSize: 6,
					Components: [new UnitStreamComponentInfo(11, "Position", 22, "Float", 0, 0, 16)]),
			}
			: Array.Empty<UnitStreamInfo>();
		var meshes = hasRawMesh
			? new[]
			{
				new UnitMeshInfo(
					Index: 7,
					Offset: 0,
					MeshId: 0xabcd,
					LodIndex: 0,
					TransformIndex: 0,
					StreamIndex: 1,
					NumMaterials: 2,
					MaterialOffset: 0,
					NumSections: 1,
					SectionsOffset: 0,
					SemanticInfo: UnitMeshSemanticInfo.Empty(0, 7),
					MaterialSlotIds: [10, 11],
					Sections: []),
			}
			: Array.Empty<UnitMeshInfo>();
		var rawMeshes = hasRawMesh
			? new[]
			{
				new UnitRawMeshData(
					MeshInfoIndex: 7,
					MeshId: 0xabcd,
					LodIndex: 0,
					StreamIndex: 1,
					Sections: [new UnitRawMeshSectionData(0, 10, [])],
					Triangles: [new UnitTriangleIndices(0, 1, 0)],
					Vertices:
					[
						new UnitRawVertexRecord(0, [], []),
						new UnitRawVertexRecord(1, [], []),
					]),
			}
			: Array.Empty<UnitRawMeshData>();
		var model = new UnitMeshModel(
			Version: 1,
			NameHash: 2,
			BonesRef: 0,
			CompositeRef: 0,
			CustomizationInfoOffset: 0,
			BoneInfoOffset: 0,
			StreamInfoOffset: 0,
			MeshInfoOffset: 0,
			MaterialsOffset: 0,
			EndingOffset: 0,
			CustomizationInfo: UnitCustomizationInfo.Empty,
			BoneInfos: [],
			Streams: streams,
			Meshes: meshes,
			Materials: [],
			RawMeshes: [],
			RawMeshData: rawMeshes);
		return new PatchUnitMesh(entry, new PatchEntryPayload(entry, [], [], []), model);
	}

	private static string CreateTempDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private sealed class FakePatchTocScanner : IPatchTocScanner
	{
		private readonly IReadOnlyList<PatchTocEntry> entries;

		public FakePatchTocScanner(IReadOnlyList<PatchTocEntry> entries)
		{
			this.entries = entries;
		}

		public ValueTask<IReadOnlySet<AssetKey>> ScanAssetKeysAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<IReadOnlySet<AssetKey>>(entries.Select(entry => entry.AssetKey).ToHashSet());

		public IReadOnlySet<AssetKey> ScanAssetKeys(ReadOnlySpan<byte> tocData, bool usesSlimEntryOffset = false)
			=> entries.Select(entry => entry.AssetKey).ToHashSet();

		public IReadOnlyList<PatchTocEntry> ScanEntries(ReadOnlySpan<byte> tocData, string sourceFilePath, bool usesSlimEntryOffset = false)
			=> entries;

		public ValueTask<IReadOnlyList<PatchTocEntry>> ScanEntriesAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(entries);
	}

	private sealed class ThrowingPatchTocScanner : IPatchTocScanner
	{
		public ValueTask<IReadOnlySet<AssetKey>> ScanAssetKeysAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
			=> throw new InvalidDataException("bad toc");

		public IReadOnlySet<AssetKey> ScanAssetKeys(ReadOnlySpan<byte> tocData, bool usesSlimEntryOffset = false)
			=> throw new InvalidDataException("bad toc");

		public IReadOnlyList<PatchTocEntry> ScanEntries(ReadOnlySpan<byte> tocData, string sourceFilePath, bool usesSlimEntryOffset = false)
			=> throw new InvalidDataException("bad toc");

		public ValueTask<IReadOnlyList<PatchTocEntry>> ScanEntriesAsync(string patchTocFilePath, CancellationToken cancellationToken = default)
			=> throw new InvalidDataException("bad toc");
	}

	private sealed class FakePatchUnitMeshReader : IPatchUnitMeshReader
	{
		private readonly IReadOnlyDictionary<PatchTocEntry, PatchUnitMesh> units;
		private readonly PatchTocEntry? failingEntry;
		private readonly string failureMessage;

		public FakePatchUnitMeshReader(
			IReadOnlyDictionary<PatchTocEntry, PatchUnitMesh> units,
			PatchTocEntry? failingEntry = null,
			string failureMessage = "bad unit")
		{
			this.units = units;
			this.failingEntry = failingEntry;
			this.failureMessage = failureMessage;
		}

		public ValueTask<PatchUnitMesh> ReadUnitMeshAsync(PatchTocEntry entry, CancellationToken cancellationToken = default)
		{
			if (entry == failingEntry)
			{
				throw new InvalidDataException(failureMessage);
			}

			return ValueTask.FromResult(units[entry]);
		}

		public ValueTask<PatchUnitMesh> ReadUnitMeshAsync(PatchTocEntry entry, IReadOnlyList<PatchTocEntry> entries, CancellationToken cancellationToken = default)
			=> ReadUnitMeshAsync(entry, cancellationToken);
	}
}
