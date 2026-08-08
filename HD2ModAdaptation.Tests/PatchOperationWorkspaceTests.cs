using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.PatchWorkspace;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;
using System.Text.Json;
using Xunit;

namespace HD2ModAdaptation.Tests;

public sealed class PatchOperationWorkspaceTests
{
	[Fact]
	public void StageMovesAllPayloadOwnershipToOperationFilesAndDisposesThem()
	{
		var output = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(output);
		string workspacePath;
		try
		{
			using (var workspace = new PatchOperationWorkspace(output, "unit-rebuild"))
			{
				workspacePath = workspace.DirectoryPath;
				var staged = workspace.Stage(new CanonicalPatchSessionEntry(
					new AssetKey(0x10, 0x20),
					CanonicalPatchEntryOwnership.TargetOutput,
					[1, 2], [3, 4], [5, 6]));

				Assert.Null(staged.TocData);
				Assert.Null(staged.GpuData);
				Assert.Null(staged.StreamData);
				Assert.Equal([1, 2], File.ReadAllBytes(staged.TocDataPath!));
				Assert.Equal([3, 4], File.ReadAllBytes(staged.GpuDataPath!));
				Assert.Equal([5, 6], File.ReadAllBytes(staged.StreamDataPath!));
				using var manifest = JsonDocument.Parse(File.ReadAllText(workspace.ManifestPath));
				Assert.Equal("unit-rebuild", manifest.RootElement.GetProperty("OperationKind").GetString());
				Assert.Equal(0x20UL, manifest.RootElement.GetProperty("Outputs")[0].GetProperty("Key").GetProperty("FileId").GetUInt64());
			}

			Assert.False(Directory.Exists(workspacePath));
		}
		finally
		{
			if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
		}
	}

	[Fact]
	public void StageBatchesManifestWritesAfterTheInitialRecoverableEntry()
	{
		var output = Path.Combine(Path.GetTempPath(), "HD2ModAdaptationTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(output);
		try
		{
			using var workspace = new PatchOperationWorkspace(output, "unit-rebuild");
			for (ulong index = 0; index < 2; index++)
				workspace.Stage(new CanonicalPatchSessionEntry(
					new AssetKey(0x10, index), CanonicalPatchEntryOwnership.TargetOutput, [1], [2], [3]));

			using var manifest = JsonDocument.Parse(File.ReadAllText(workspace.ManifestPath));
			Assert.Single(manifest.RootElement.GetProperty("Outputs").EnumerateArray());
		}
		finally
		{
			if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
		}
	}
}
