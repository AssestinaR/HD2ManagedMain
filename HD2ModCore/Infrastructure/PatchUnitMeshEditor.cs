using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：执行 patch-level Unit mesh dry-run 编辑，生成更新后的 TOC payload 与 GPU sidecar 数据但不写文件。
// Purpose: Performs patch-level Unit mesh dry-run edits, producing updated TOC payload and GPU sidecar data without writing files.
public sealed class PatchUnitMeshEditor : IPatchUnitMeshEditor
{
	private readonly IPatchUnitMeshReader unitMeshReader;
	private readonly IUnitMeshMinifier minifier;
	private readonly IUnitMeshRetargeter retargeter;
	private readonly IUnitMeshWriter writer;

	public PatchUnitMeshEditor(
		IPatchUnitMeshReader unitMeshReader,
		IUnitMeshMinifier minifier,
		IUnitMeshRetargeter retargeter,
		IUnitMeshWriter writer)
	{
		this.unitMeshReader = unitMeshReader ?? throw new ArgumentNullException(nameof(unitMeshReader));
		this.minifier = minifier ?? throw new ArgumentNullException(nameof(minifier));
		this.retargeter = retargeter ?? throw new ArgumentNullException(nameof(retargeter));
		this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
	}

	public async ValueTask<PatchUnitMeshEditResult> MinifyAllAsync(PatchTocEntry entry, CancellationToken cancellationToken = default)
	{
		var unit = await unitMeshReader.ReadUnitMeshAsync(entry, cancellationToken).ConfigureAwait(false);
		var editedModel = minifier.MinifyAll(unit.Model);
		return BuildResult(unit, editedModel);
	}

	public async ValueTask<PatchUnitMeshEditResult> MinifyRawMeshAsync(PatchTocEntry entry, int meshInfoIndex, CancellationToken cancellationToken = default)
	{
		var unit = await unitMeshReader.ReadUnitMeshAsync(entry, cancellationToken).ConfigureAwait(false);
		var editedModel = minifier.MinifyRawMesh(unit.Model, meshInfoIndex);
		return BuildResult(unit, editedModel);
	}

	public async ValueTask<PatchUnitMeshEditResult> ReplaceRawMeshAsync(
		PatchTocEntry targetEntry,
		int targetMeshInfoIndex,
		PatchTocEntry sourceEntry,
		int sourceMeshInfoIndex,
		CancellationToken cancellationToken = default)
	{
		var targetUnit = await unitMeshReader.ReadUnitMeshAsync(targetEntry, cancellationToken).ConfigureAwait(false);
		var sourceUnit = await unitMeshReader.ReadUnitMeshAsync(sourceEntry, cancellationToken).ConfigureAwait(false);
		var editedModel = retargeter.ReplaceRawMesh(targetUnit.Model, targetMeshInfoIndex, sourceUnit.Model, sourceMeshInfoIndex);
		return BuildResult(targetUnit, editedModel);
	}

	private PatchUnitMeshEditResult BuildResult(PatchUnitMesh unit, UnitMeshModel editedModel)
	{
		var written = unit.CompositePayload is null
			? writer.Write(editedModel, unit.Payload.TocData)
			: writer.Write(editedModel, unit.Payload.TocData, unit.CompositePayload.TocData);
		return new PatchUnitMeshEditResult(
			unit.Entry,
			unit.Payload,
			unit.Model,
			editedModel,
			written.TocData,
			unit.CompositePayload is null ? written.GpuData : Array.Empty<byte>(),
			unit.CompositePayload?.Entry.AssetKey,
			written.CompositeTocData,
			unit.CompositePayload is null ? null : written.GpuData);
	}
}
