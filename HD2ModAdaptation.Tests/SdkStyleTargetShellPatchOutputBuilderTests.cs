using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies multiple fully reconstructed target Units become distinct patch additions without source Unit entries.
public sealed class SdkStyleTargetShellPatchOutputBuilderTests
{
	[Fact]
	public void Build_RejectsDuplicateTargetUnitsBeforePatchWrite()
	{
		var target = new GameDataUnitMesh(new AssetKey(PatchUnitMeshReader.UnitTypeId, 2), "target", new PatchEntryPayload(new PatchTocEntry(new AssetKey(PatchUnitMeshReader.UnitTypeId, 2), "target", "target"), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>()), EmptyModel());
		var item = new SdkStyleTargetShellPatchWorkItem(target, Array.Empty<PatchUnitMesh>(), Array.Empty<TargetShellMeshMapping>());

		Assert.Throws<InvalidDataException>(() => new SdkStyleTargetShellPatchOutputBuilder().Build(new[] { item, item }));
	}

	[Fact]
	public async Task Build_CancelsWhileProcessingWorkItems()
	{
		using var cancellation = new CancellationTokenSource();
		var items = Enumerable.Range(1, 256).Select(fileId =>
		{
			var target = new GameDataUnitMesh(new AssetKey(PatchUnitMeshReader.UnitTypeId, (ulong)fileId), "target", new PatchEntryPayload(new PatchTocEntry(new AssetKey(PatchUnitMeshReader.UnitTypeId, (ulong)fileId), "target", "target"), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>()), EmptyModel());
			return new SdkStyleTargetShellPatchWorkItem(target, Array.Empty<PatchUnitMesh>(), Array.Empty<TargetShellMeshMapping>());
		}).ToArray();
		var build = Task.Run(() => new SdkStyleTargetShellPatchOutputBuilder().Build(new CancelingCollection(items, cancellation), cancellation.Token));

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await build);
	}

	private static UnitMeshModel EmptyModel()
		=> new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), Array.Empty<UnitStreamInfo>(), Array.Empty<UnitMeshInfo>(), Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), Array.Empty<UnitRawMeshData>());

	private sealed class CancelingCollection(IReadOnlyList<SdkStyleTargetShellPatchWorkItem> items, CancellationTokenSource cancellation) : IReadOnlyCollection<SdkStyleTargetShellPatchWorkItem>
	{
		public int Count => items.Count;
		public IEnumerator<SdkStyleTargetShellPatchWorkItem> GetEnumerator()
		{
			using var enumerator = items.GetEnumerator();
			if (enumerator.MoveNext())
			{
				cancellation.Cancel();
				yield return enumerator.Current;
				while (enumerator.MoveNext()) yield return enumerator.Current;
			}
		}
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}
}