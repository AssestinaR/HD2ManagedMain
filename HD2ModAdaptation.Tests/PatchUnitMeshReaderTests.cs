using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies the explicit patch-entry Unit reader rejects invalid resource contracts before adaptation.
public sealed class PatchUnitMeshReaderTests
{
	[Fact]
	public async Task ReadAsync_NonUnitEntry_ThrowsBeforeReadingPayload()
	{
		var entry = new PatchTocEntry(new AssetKey(0x1111, 0x2222), "unused.patch", "unused.patch");

		var exception = await Assert.ThrowsAsync<InvalidDataException>(() => new PatchUnitMeshReader().ReadAsync(entry, Array.Empty<PatchTocEntry>()).AsTask());

		Assert.Contains("not a Unit resource", exception.Message);
	}

	[Fact]
	public async Task ReadAsync_ReferencedCompositeIsMissingUnderStrictPolicy_Throws()
	{
		var entry = new PatchTocEntry(new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1234), "unused.patch", "unused.patch");
		var payloadReader = new FixedPayloadReader(new PatchEntryPayload(entry, CreateUnitHeader(compositeReference: 0x4567), Array.Empty<byte>(), Array.Empty<byte>()));
		var reader = new PatchUnitMeshReader(payloadReader);

		var exception = await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(entry, new[] { entry }).AsTask());

		Assert.Contains("Composite asset 0x0000000000004567", exception.Message);
	}

	[Fact]
	public async Task ReadAsync_ExternalCompositePolicy_PreservesUnresolvedReferenceForInlineUnit()
	{
		var entry = new PatchTocEntry(new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1234), "unused.patch", "unused.patch");
		var payloadReader = new FixedPayloadReader(new PatchEntryPayload(entry, CreateInlineUnitToc(compositeReference: 0x4567, bonesReference: 0x89ab), Array.Empty<byte>(), Array.Empty<byte>()));
		var reader = new PatchUnitMeshReader(payloadReader);

		var result = await reader.ReadAsync(entry, new[] { entry }, PatchUnitDependencyPolicy.AllowExternalCompositeReference);

		Assert.Null(result.CompositePayload);
		Assert.NotNull(result.Dependencies);
		Assert.Equal(0x4567UL, result.Dependencies!.CompositeReference);
		Assert.Equal(0x89abUL, result.Dependencies.BonesReference);
		Assert.True(result.Dependencies.HasUnresolvedExternalComposite);
		Assert.True(result.Dependencies.HasUnresolvedExternalBone);
	}

	[Fact]
	public async Task ReadCanonicalSourceAsync_DropsPayloadBytesButPreservesModelAndDependencies()
	{
		var entry = new PatchTocEntry(new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1234), "unused.patch", "unused.patch");
		var payload = new PatchEntryPayload(entry, CreateInlineUnitToc(compositeReference: 0, bonesReference: 0), new byte[] { 1 }, new byte[] { 2, 3, 4 });
		var reader = new PatchUnitMeshReader(new FixedPayloadReader(payload));

		var result = await reader.ReadCanonicalSourceAsync(entry, new[] { entry });

		Assert.NotNull(result.Model);
		Assert.Empty(result.Payload.TocData);
		Assert.Empty(result.Payload.StreamData);
		Assert.Empty(result.Payload.GpuResourceData);
		Assert.Null(result.CompositePayload);
		Assert.NotNull(result.Dependencies);
	}

	private static byte[] CreateUnitHeader(ulong compositeReference)
	{
		var data = new byte[24];
		BitConverter.GetBytes(compositeReference).CopyTo(data, 16);
		return data;
	}

	private static byte[] CreateInlineUnitToc(ulong compositeReference, ulong bonesReference)
	{
		var data = new byte[136];
		BitConverter.GetBytes(bonesReference).CopyTo(data, 8);
		BitConverter.GetBytes(compositeReference).CopyTo(data, 16);
		BitConverter.GetBytes(1U).CopyTo(data, 0x2c);
		BitConverter.GetBytes(96U).CopyTo(data, 0x5c);
		BitConverter.GetBytes(112U).CopyTo(data, 0x64);
		BitConverter.GetBytes(0U).CopyTo(data, 0x70);
		return data;
	}

	private sealed class FixedPayloadReader(PatchEntryPayload payload) : IPatchEntryPayloadReader
	{
		public ValueTask<PatchEntryPayload> ReadPayloadAsync(PatchTocEntry entry, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(payload);
	}
}
