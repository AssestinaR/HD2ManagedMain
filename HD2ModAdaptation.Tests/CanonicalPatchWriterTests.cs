using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using System.Buffers.Binary;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies canonical finalized-session payload ownership and independent patch layout.
public sealed class CanonicalPatchWriterTests : IDisposable
{
	private readonly string directory = Path.Combine(Path.GetTempPath(), "canonical-writer-" + Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task Writer_RequiresFinalizedSession()
	{
		var session = new CanonicalPatchSession();
		session.AddEntry(Entry(1, 2, [1], [2], [3]));

		await Assert.ThrowsAsync<InvalidOperationException>(() => new CanonicalPatchWriter().WriteAsync(session, directory).AsTask());
	}

	[Fact]
	public async Task Writer_ComputesPayloadOffsetsAnd64ByteAlignment()
	{
		var session = new CanonicalPatchSession();
		session.AddEntry(Entry(1, 2, [1, 2, 3], [4, 5], [6, 7, 8]));
		session.AddEntry(Entry(1, 3, [9], [], [10, 11, 12, 13]));
		Assert.True(session.Finalize(CanonicalDependencyClosureValidation.Valid).IsValid);

		var result = await new CanonicalPatchWriter().WriteAsync(session, directory).AsTask();
		var entries = await new PatchTocScanner().ScanEntriesAsync(result.TocFilePath);

		Assert.Equal(2, entries.Count);
		Assert.Equal(new[] { 1u, 2u }, entries.Select(entry => entry.EntryIndex));
		Assert.All(entries, entry =>
		{
			Assert.InRange((long)entry.TocDataOffset + entry.TocDataSize, 0L, result.TocFileSize);
			if (entry.GpuResourceSize != 0) Assert.Equal(0UL, entry.GpuResourceOffset % 64);
			if (entry.StreamSize != 0) Assert.Equal(0UL, entry.StreamOffset % 64);
		});
		Assert.Equal(3u, entries[0].TocDataSize);
		Assert.Equal(1u, entries[1].TocDataSize);
	}

	[Fact]
	public async Task Writer_UsesSdk72ByteHeaderAndMinimumTocLength()
	{
		var session = new CanonicalPatchSession();
		session.AddEntry(Entry(7, 2, [1], [], []));
		Assert.True(session.Finalize(CanonicalDependencyClosureValidation.Valid).IsValid);

		var result = await new CanonicalPatchWriter().WriteAsync(session, directory).AsTask();
		var toc = await File.ReadAllBytesAsync(result.TocFilePath);

		Assert.Equal(4026531857u, BinaryPrimitives.ReadUInt32LittleEndian(toc.AsSpan(0, 4)));
		Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(toc.AsSpan(8, 4)));
		Assert.True(toc.Length >= 256);
		Assert.Equal(7UL, BinaryPrimitives.ReadUInt64LittleEndian(toc.AsSpan(72 + 8, 8)));
		Assert.Equal(2UL, BinaryPrimitives.ReadUInt64LittleEndian(toc.AsSpan(72 + 32, 8)));
	}

	[Fact]
	public async Task Writer_GroupsInterleavedEntriesByTypeAndPreservesSessionOrderWithinType()
	{
		var session = new CanonicalPatchSession();
		session.AddEntry(Entry(2, 20, [20], [], []));
		session.AddEntry(Entry(1, 10, [10], [], []));
		session.AddEntry(Entry(2, 21, [21], [], []));
		Assert.True(session.Finalize(CanonicalDependencyClosureValidation.Valid).IsValid);

		var result = await new CanonicalPatchWriter().WriteAsync(session, directory).AsTask();
		var entries = await new PatchTocScanner().ScanEntriesAsync(result.TocFilePath);

		Assert.Equal(new[] { 20UL, 21UL, 10UL }, entries.Select(entry => entry.AssetKey.FileId));
		Assert.Equal(new[] { 1u, 2u, 3u }, entries.Select(entry => entry.EntryIndex));
	}

	[Fact]
	public async Task Writer_RejectsUnvalidatedDependencyClosure()
	{
		var session = new CanonicalPatchSession();
		session.AddEntry(Entry(1, 2, [1], [], []));
		Assert.False(session.Finalize(CanonicalDependencyClosureValidation.Invalid).IsValid);

		await Assert.ThrowsAsync<InvalidOperationException>(() => new CanonicalPatchWriter().WriteAsync(session, directory).AsTask());
	}

	[Fact]
	public async Task Writer_RejectsSourceRetainedEntry()
	{
		var session = new CanonicalPatchSession();
		Assert.Throws<InvalidOperationException>(() => session.AddEntry(Entry(1, 2, [1], [2], [3], CanonicalPatchEntryOwnership.SourceRetained)));
		await Task.CompletedTask;
	}

	[Fact]
	public async Task Writer_RejectsEmptyTargetOutputPayload()
	{
		var session = new CanonicalPatchSession();
		session.AddEntry(new(new AssetKey(1, 2), CanonicalPatchEntryOwnership.TargetOutput, null, [], []));
		var validation = session.Finalize(CanonicalDependencyClosureValidation.Valid);

		Assert.False(validation.IsValid);
		Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "MissingEntryPayload");
		await Assert.ThrowsAsync<InvalidOperationException>(() => new CanonicalPatchWriter().WriteAsync(session, directory).AsTask());
	}

	private static CanonicalPatchSessionEntry Entry(ulong typeId, ulong fileId, byte[] toc, byte[] gpu, byte[] stream, CanonicalPatchEntryOwnership ownership = CanonicalPatchEntryOwnership.TargetOutput)
		=> new(new AssetKey(typeId, fileId), ownership, toc, gpu, stream);

	public void Dispose()
	{
		if (Directory.Exists(directory)) Directory.Delete(directory, true);
	}
}