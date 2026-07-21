using System.Buffers.Binary;
using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModCore.Application;
using HD2ModCore.Domain;

// Purpose: Performs the smallest safe Unit compatibility read: four bytes at Unit TOC offset 0x2c.
namespace HD2ModCore.Infrastructure;

public sealed class UnitVersionProbe : IUnitVersionProbe
{
	private const int VersionOffset = 0x2c;
	private const int RequiredBytes = VersionOffset + sizeof(uint);

	public async ValueTask<IReadOnlyList<UnitVersionEvidence>> ProbeAsync(PatchGroupAnalysis analysis, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(analysis);
		var evidence = new List<UnitVersionEvidence>();
		foreach (var entry in analysis.Entries.Where(entry => entry.AssetKey.TypeId == PatchUnitMeshReader.UnitTypeId))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				if (entry.TocDataSize < RequiredBytes) throw new InvalidDataException("Unit TOC payload is shorter than the version field.");
				var bytes = new byte[RequiredBytes];
				await using var stream = new FileStream(entry.SourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, RequiredBytes, FileOptions.Asynchronous | FileOptions.RandomAccess);
				if (entry.TocDataOffset > (ulong)stream.Length || entry.TocDataOffset + RequiredBytes > (ulong)stream.Length) throw new InvalidDataException("Unit TOC payload range is outside the patch file.");
				stream.Position = checked((long)entry.TocDataOffset);
				await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
				evidence.Add(new UnitVersionEvidence(entry.SourceFileName, new AssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(VersionOffset))));
			}
			catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or OverflowException)
			{
				evidence.Add(new UnitVersionEvidence(entry.SourceFileName, new AssetKey(entry.AssetKey.TypeId, entry.AssetKey.FileId), null, exception.Message));
			}
		}
		return evidence;
	}
}