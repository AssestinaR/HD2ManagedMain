using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从 patch 文件夹解析 Unit entry，构建可供自动化 dry-run 选择 source mesh 的 catalog。
// Purpose: Parses Unit entries from a patch directory and builds a catalog of source meshes selectable for automation dry-runs.
public sealed class PatchUnitMeshSourceCatalogBuilder : IPatchUnitMeshSourceCatalogBuilder
{
	private const string NoReadableRawMeshReason = "Unit entry does not contain readable RawMesh data.";
	private const string InvalidPayloadRangeReason = "Unit entry payload range is outside its patch payload files.";
	private const string UnsupportedNoBufferStreamReason = "Unsupported Unit mesh format: StreamInfoOffset is zero and no CompositeRef is present.";
	private const int SidecarAlignment = 64;

	private readonly IPatchTocFileCollector patchTocFileCollector;
	private readonly IPatchTocScanner patchTocScanner;
	private readonly IPatchUnitMeshReader patchUnitMeshReader;

	public PatchUnitMeshSourceCatalogBuilder(
		IPatchTocFileCollector patchTocFileCollector,
		IPatchTocScanner patchTocScanner,
		IPatchUnitMeshReader patchUnitMeshReader)
	{
		this.patchTocFileCollector = patchTocFileCollector ?? throw new ArgumentNullException(nameof(patchTocFileCollector));
		this.patchTocScanner = patchTocScanner ?? throw new ArgumentNullException(nameof(patchTocScanner));
		this.patchUnitMeshReader = patchUnitMeshReader ?? throw new ArgumentNullException(nameof(patchUnitMeshReader));
	}

	public async ValueTask<PatchUnitMeshSourceCatalog> BuildCatalogAsync(
		string patchDirectoryPath,
		CancellationToken cancellationToken = default)
	{
		var fileSet = patchTocFileCollector.Collect(patchDirectoryPath);
		var entries = new List<PatchUnitMeshSourceCatalogEntry>();
		var failures = new List<PatchUnitMeshSourceCatalogFailure>();
		var entriesByPath = new Dictionary<string, IReadOnlyList<PatchTocEntry>>(StringComparer.OrdinalIgnoreCase);
		var allScannedEntries = new List<PatchTocEntry>();

		var validatesPayloadRanges = this.patchUnitMeshReader is PatchUnitMeshReader;
		foreach (var patchTocFilePath in fileSet.PatchTocFilePaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<PatchTocEntry> scannedEntries;
			try
			{
				scannedEntries = await patchTocScanner.ScanEntriesAsync(patchTocFilePath, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				failures.Add(new PatchUnitMeshSourceCatalogFailure(
					new PatchTocEntry(new AssetKey(PatchUnitMeshReader.UnitTypeId, 0), patchTocFilePath, Path.GetFileName(patchTocFilePath)),
					ex.Message,
					ex));
				continue;
			}

			entriesByPath[patchTocFilePath] = scannedEntries;
			allScannedEntries.AddRange(scannedEntries);
		}

		foreach (var patchTocFilePath in fileSet.PatchTocFilePaths)
		{
			if (!entriesByPath.TryGetValue(patchTocFilePath, out var scannedEntries))
			{
				continue;
			}

			foreach (var entry in scannedEntries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (validatesPayloadRanges && !HasValidPayloadRanges(entry))
				{
					failures.Add(new PatchUnitMeshSourceCatalogFailure(
						entry,
						InvalidPayloadRangeReason,
						IsUnsupportedUnitMeshFormat: true));
					continue;
				}

				try
				{
					var patchUnitMesh = await patchUnitMeshReader.ReadUnitMeshAsync(entry, allScannedEntries, cancellationToken).ConfigureAwait(false);
					var meshSummaries = BuildMeshSummaries(patchUnitMesh.Model).ToArray();
					if (meshSummaries.Length == 0)
					{
						failures.Add(new PatchUnitMeshSourceCatalogFailure(
							entry,
							NoReadableRawMeshReason,
							IsUnsupportedUnitMeshFormat: true));
						continue;
					}

					entries.Add(new PatchUnitMeshSourceCatalogEntry(
						entry,
						patchUnitMesh.Model.Version,
						patchUnitMesh.Model.NameHash,
						meshSummaries));
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					failures.Add(new PatchUnitMeshSourceCatalogFailure(
						entry,
						ex.Message,
						ex,
						IsUnsupportedUnitMeshFormat: IsUnsupportedUnitMeshFormat(ex)));
				}
			}
		}

		return new PatchUnitMeshSourceCatalog(fileSet.RootDirectoryPath, fileSet.PatchTocFilePaths, allScannedEntries, entries, failures);
	}

	private static bool IsUnsupportedUnitMeshFormat(Exception exception)
		=> exception is InvalidDataException && exception.Message == UnsupportedNoBufferStreamReason;

	private static bool HasValidPayloadRanges(PatchTocEntry entry)
	{
		var patchLength = new FileInfo(entry.SourceFilePath).Length;
		var gpuLength = GetOptionalFileLength(entry.SourceFilePath + ".gpu_resources");
		var streamLength = GetOptionalFileLength(entry.SourceFilePath + ".stream");
		return IsRangeValid(patchLength, entry.TocDataOffset, entry.TocDataSize) &&
			IsSidecarRangeValid(gpuLength, entry.GpuResourceOffset, entry.GpuResourceSize) &&
			IsSidecarRangeValid(streamLength, entry.StreamOffset, entry.StreamSize);
	}

	private static long GetOptionalFileLength(string path)
		=> File.Exists(path) ? new FileInfo(path).Length : 0L;

	private static bool IsRangeValid(long containerLength, ulong offset, uint size)
	{
		if (size == 0)
		{
			return true;
		}

		return offset <= (ulong)containerLength && offset + size <= (ulong)containerLength;
	}

	private static bool IsSidecarRangeValid(long containerLength, ulong offset, uint size)
	{
		if (size == 0)
		{
			return true;
		}

		if (containerLength <= 0)
		{
			return false;
		}

		var alignedLength = AlignUp((ulong)containerLength, SidecarAlignment);
		return offset <= alignedLength && offset + size <= alignedLength;
	}

	private static ulong AlignUp(ulong value, int alignment)
	{
		var mask = checked((ulong)alignment - 1UL);
		return (value + mask) & ~mask;
	}

	private static IEnumerable<PatchUnitMeshSourceMeshSummary> BuildMeshSummaries(UnitMeshModel model)
	{
		foreach (var rawMesh in model.RawMeshData)
		{
			var stream = model.Streams.FirstOrDefault(candidate => candidate.Index == rawMesh.StreamIndex);
			var meshInfo = model.Meshes.FirstOrDefault(candidate => candidate.Index == rawMesh.MeshInfoIndex);
			var componentLayout = stream?.Components
				.Select(component => new UnitMeshReplacementComponentSignature(component.Type, component.Format, component.Index, component.Size))
				.ToArray()
				?? Array.Empty<UnitMeshReplacementComponentSignature>();

			yield return new PatchUnitMeshSourceMeshSummary(
				rawMesh.MeshInfoIndex,
				rawMesh.MeshId,
				rawMesh.LodIndex,
				rawMesh.StreamIndex,
				(uint)rawMesh.Vertices.Count,
				(uint)(rawMesh.Triangles.Count * 3),
				meshInfo?.NumMaterials ?? 0,
				(uint)rawMesh.Sections.Count,
				stream?.VertexStride ?? 0,
				componentLayout);
		}
	}
}
