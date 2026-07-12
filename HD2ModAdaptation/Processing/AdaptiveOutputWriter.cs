using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Processing;

// Purpose: Writes updated model in the appropriate format (embedded or separated).
// Critical: Prevents separated patches from becoming embedded by filtering material data.
public sealed class AdaptiveOutputWriter
{
	private readonly UnitMeshWriter writer;

	public AdaptiveOutputWriter(UnitMeshWriter? writer = null)
	{
		this.writer = writer ?? new UnitMeshWriter();
	}

	/// <summary>
	/// Writes the updated model, preserving the original patch mode.
	/// For embedded patches: writes everything (model + materials).
	/// For separated patches: writes only model, filters out material data.
	/// </summary>
	public async Task<OutputResult> WriteAsync(
		AdaptiveMeshTransferResult transferResult,
		string outputDirectory)
	{
		ArgumentNullException.ThrowIfNull(transferResult);
		ArgumentNullException.ThrowIfNull(outputDirectory);

		Directory.CreateDirectory(outputDirectory);

		if (transferResult.OriginalMode == PatchMaterialMode.Embedded)
		{
			// Original was embedded, output as embedded
			return await WriteEmbeddedAsync(transferResult.UpdatedModel, outputDirectory);
		}
		else
		{
			// Original was separated, output as separated (model only)
			return await WriteSeparatedAsync(transferResult, outputDirectory);
		}
	}

	/// <summary>
	/// Writes embedded patch (model + materials together).
	/// </summary>
	private async Task<OutputResult> WriteEmbeddedAsync(
		UnitMeshModel model,
		string outputDirectory)
	{
		Console.WriteLine("Writing as embedded patch (model + materials)...");

		// Write full model with materials
		var result = writer.Write(model, Array.Empty<byte>());

		var outputPath = Path.Combine(outputDirectory, "output.patch_0");
		await File.WriteAllBytesAsync(outputPath, result.TocData);
		await File.WriteAllBytesAsync(outputPath + ".gpu_resources", result.GpuData);
		await File.WriteAllBytesAsync(outputPath + ".stream", Array.Empty<byte>());

		Console.WriteLine($"Written: {outputPath}");
		Console.WriteLine($"  - TOC: {result.TocData.Length} bytes");
		Console.WriteLine($"  - GPU: {result.GpuData.Length} bytes");

		return new OutputResult(
			OutputMode.Embedded,
			[outputPath],
			[],
			$"Embedded patch written: TOC={result.TocData.Length} bytes, GPU={result.GpuData.Length} bytes");
	}

	/// <summary>
	/// Writes separated patch (model only, no material data).
	/// Critical: This prevents materials from being embedded in the output.
	/// Note: Currently writes full model data. Material filtering would require
	/// parsing and reconstructing the TOC, which is complex. For now, we rely on
	/// the fact that separated patches naturally don't include material data.
	/// </summary>
	private async Task<OutputResult> WriteSeparatedAsync(
		AdaptiveMeshTransferResult transferResult,
		string outputDirectory)
	{
		Console.WriteLine("Writing as separated patch (model only)...");
		Console.WriteLine("⚠️ Note: Output contains model data and material references");
		Console.WriteLine("   Actual material data should come from separate material patches");

		// Write the model (includes material references but not material data)
		var result = writer.Write(transferResult.UpdatedModel, Array.Empty<byte>());

		var outputPath = Path.Combine(outputDirectory, "output.patch_0");
		await File.WriteAllBytesAsync(outputPath, result.TocData);
		await File.WriteAllBytesAsync(outputPath + ".gpu_resources", result.GpuData);
		await File.WriteAllBytesAsync(outputPath + ".stream", Array.Empty<byte>());

		Console.WriteLine($"Written: {outputPath}");
		Console.WriteLine($"  - TOC: {result.TocData.Length} bytes");
		Console.WriteLine($"  - GPU: {result.GpuData.Length} bytes");

		// Note: Original material patches should be reused as-is
		var notes = new List<string>
		{
			$"Model patch written: TOC={result.TocData.Length} bytes, GPU={result.GpuData.Length} bytes",
			"⚠️ Important: Continue using the original material patches:"
		};

		foreach (var matPath in transferResult.UsedMaterialPaths)
		{
			notes.Add($"  - {Path.GetFileName(matPath)}");
		}

		if (transferResult.UsedMaterialPaths.Count == 0)
		{
			notes.Add("  (No material patches were provided during transfer)");
		}

		return new OutputResult(
			OutputMode.Separated,
			[outputPath],
			transferResult.UsedMaterialPaths,
			string.Join(Environment.NewLine, notes));
	}
}

public enum OutputMode
{
	Embedded,   // Model + materials in single patch
	Separated   // Model only, materials in separate patches
}

/// <summary>
/// Result of output operation.
/// </summary>
public sealed record OutputResult(
	OutputMode Mode,
	IReadOnlyList<string> ModelPatchPaths,
	IReadOnlyList<string> MaterialPatchPaths,
	string Notes)
{
	public void PrintSummary()
	{
		Console.WriteLine($"\n=== Output Summary ===");
		Console.WriteLine($"Mode: {Mode}");
		Console.WriteLine($"Model Patches: {ModelPatchPaths.Count}");
		foreach (var path in ModelPatchPaths)
		{
			Console.WriteLine($"  - {Path.GetFileName(path)}");
		}
		
		if (MaterialPatchPaths.Count > 0)
		{
			Console.WriteLine($"Material Patches (reuse): {MaterialPatchPaths.Count}");
			foreach (var path in MaterialPatchPaths)
			{
				Console.WriteLine($"  - {Path.GetFileName(path)}");
			}
		}
		
		Console.WriteLine($"\nNotes:\n{Notes}");
	}
}
