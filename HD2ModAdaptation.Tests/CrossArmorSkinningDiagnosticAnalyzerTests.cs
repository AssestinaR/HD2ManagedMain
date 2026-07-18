using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies cross-armor skinning diagnostics distinguish active remap failures from ignorable low-weight influences.
public sealed class CrossArmorSkinningDiagnosticAnalyzerTests
{
	[Fact]
	public void Analyze_IgnoresLowWeightInfluenceButReportsInvalidActiveInfluence()
	{
		var model = CreateModel(
			indices: [0, 1, 2, 0],
			weights: [0.8f, 0.1995f, 0.0005f, 0f],
			fakeIndices: [0, 0]);

		var diagnostic = new CrossArmorSkinningDiagnosticAnalyzer().Analyze(model, 0);

		Assert.Equal(1, diagnostic.IgnoredLowWeightInfluenceCount);
		Assert.Equal(0, diagnostic.InvalidActiveInfluenceCount);
		Assert.Equal(2, diagnostic.ActiveInfluenceCount);
	}

	[Fact]
	public void Analyze_ReportsActiveIndexOutsideMaterialRemap()
	{
		var model = CreateModel(
			indices: [0, 2, 0, 0],
			weights: [0.5f, 0.5f, 0f, 0f],
			fakeIndices: [0]);

		var diagnostic = new CrossArmorSkinningDiagnosticAnalyzer().Analyze(model, 0);

		Assert.Equal(1, diagnostic.InvalidActiveInfluenceCount);
		Assert.Contains(diagnostic.Samples.SelectMany(sample => sample.Influences), influence => influence.Failure is not null);
	}

	private static UnitMeshModel CreateModel(uint[] indices, float[] weights, IReadOnlyList<uint> fakeIndices)
	{
		var components = new UnitVertexComponentValue[]
		{
			new(0, "position", 2, "vec3_float", 0, [0f, 1f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
			new(6, "blend_indices", 0, "vec4_uint8", 0, Array.Empty<float>(), indices, Array.Empty<byte>()),
			new(7, "blend_weights", 0, "vec4_float", 0, weights, Array.Empty<uint>(), Array.Empty<byte>())
		};
		var rawMesh = new UnitRawMeshData(0, 1, 0, 0, [new UnitRawMeshSectionData(0, 20, [new UnitTriangleIndices(0, 0, 0)])], [new UnitTriangleIndices(0, 0, 0)], [new UnitRawVertexRecord(0, Array.Empty<byte>(), components)]);
		var mesh = new UnitMeshInfo(0, 0, 1, 0, 0, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(0, 0), [20], Array.Empty<UnitMeshSectionInfo>());
		var boneInfo = new UnitBoneInfo(0, 0, 2, 0, 0, 0, [0, 1], [new UnitBoneRemap(0, 0, fakeIndices)]);
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, UnitCustomizationInfo.Empty, [boneInfo], Array.Empty<UnitStreamInfo>(), [mesh], Array.Empty<UnitMaterialBinding>(), Array.Empty<UnitRawMeshSummary>(), [rawMesh])
		{
			TransformNameHashes = [0x10, 0x20],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), Array.Empty<UnitTransformMatrix>(), Array.Empty<UnitTransformEntry>(), [0x10, 0x20])
		};
	}
}