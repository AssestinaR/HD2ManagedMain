using System.Globalization;
using System.Text;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

namespace HD2ModCore.Infrastructure;

// Purpose: Provides one comparable per-Unit telemetry format for Canonical workflows.
public sealed record CanonicalUnitJobTelemetryRow(
	string Flow,
	int Sequence,
	ulong UnitFileId,
	bool HiddenCacheHit,
	bool PlannedReplacement,
	int MeshCount,
	int VertexCount,
	int TriangleCount,
	TimeSpan SourceRead,
	TimeSpan TargetRead,
	TimeSpan Mapping,
	TimeSpan Transform,
	TimeSpan MeshAssembly,
	CanonicalMeshAssemblyTelemetry MeshBreakdown,
	TimeSpan BonePalette,
	TimeSpan StreamContract,
	TimeSpan FinalPreparation,
	TimeSpan MaterialBindings,
	TimeSpan Serialization,
	CanonicalUnitSerializationTelemetry SerializationBreakdown,
	TimeSpan Staging,
	TimeSpan Total,
	long AllocatedBytes,
	long ManagedHeapBytes,
	long WorkingSetBytes,
	int Gen0Collections,
	int Gen1Collections,
	int Gen2Collections);

public static class CanonicalUnitJobTelemetry
{
	public static async ValueTask WriteCsvAsync(
		string path,
		IReadOnlyList<CanonicalUnitJobTelemetryRow> rows,
		CancellationToken cancellationToken)
	{
		var builder = new StringBuilder();
		builder.AppendLine("flow,sequence,unit_file_id,hidden_cache_hit,planned_replacement,mesh_count,vertex_count,triangle_count,source_read_ms,target_read_ms,mapping_ms,transform_ms,mesh_assembly_ms,mesh_route_ms,mesh_merge_ms,mesh_minify_ms,mesh_material_resolution_ms,bone_palette_ms,stream_contract_ms,prepare_final_ms,material_bindings_ms,serialization_ms,serialization_setup_ms,serialization_gpu_write_ms,serialization_toc_rebuild_ms,serialization_model_finalize_ms,staging_ms,total_ms,allocated_bytes,managed_heap_bytes,working_set_bytes,gen0_collections,gen1_collections,gen2_collections");
		foreach (var row in rows.OrderBy(row => row.Sequence))
		{
			builder.Append(row.Flow).Append(',')
				.Append(row.Sequence).Append(',')
				.Append($"0x{row.UnitFileId:x16}").Append(',')
				.Append(row.HiddenCacheHit ? '1' : '0').Append(',')
				.Append(row.PlannedReplacement ? '1' : '0').Append(',')
				.Append(row.MeshCount).Append(',').Append(row.VertexCount).Append(',').Append(row.TriangleCount).Append(',')
				.Append(Milliseconds(row.SourceRead)).Append(',').Append(Milliseconds(row.TargetRead)).Append(',')
				.Append(Milliseconds(row.Mapping)).Append(',').Append(Milliseconds(row.Transform)).Append(',')
				.Append(Milliseconds(row.MeshAssembly)).Append(',').Append(Milliseconds(row.MeshBreakdown.Route)).Append(',')
				.Append(Milliseconds(row.MeshBreakdown.Merge)).Append(',').Append(Milliseconds(row.MeshBreakdown.Minify)).Append(',')
				.Append(Milliseconds(row.MeshBreakdown.MaterialResolution)).Append(',').Append(Milliseconds(row.BonePalette)).Append(',')
				.Append(Milliseconds(row.StreamContract)).Append(',').Append(Milliseconds(row.FinalPreparation)).Append(',')
				.Append(Milliseconds(row.MaterialBindings)).Append(',').Append(Milliseconds(row.Serialization)).Append(',')
				.Append(Milliseconds(row.SerializationBreakdown.Setup)).Append(',').Append(Milliseconds(row.SerializationBreakdown.GpuWrite)).Append(',')
				.Append(Milliseconds(row.SerializationBreakdown.TocRebuild)).Append(',').Append(Milliseconds(row.SerializationBreakdown.ModelFinalize)).Append(',')
				.Append(Milliseconds(row.Staging)).Append(',').Append(Milliseconds(row.Total)).Append(',')
				.Append(row.AllocatedBytes).Append(',').Append(row.ManagedHeapBytes).Append(',').Append(row.WorkingSetBytes).Append(',')
				.Append(row.Gen0Collections).Append(',').Append(row.Gen1Collections).Append(',').Append(row.Gen2Collections).AppendLine();
		}

		await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
	}

	private static string Milliseconds(TimeSpan value)
		=> value.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture);
}
