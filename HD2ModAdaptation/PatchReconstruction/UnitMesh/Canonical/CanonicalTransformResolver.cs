using System.Numerics;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Resolves source and target mesh-local transforms from their TransformInfo indices.
// SDK reference: GetMeshData uses the mesh TransformInfo matrix as the object origin; the
// source geometry is expressed in target local space by sourceMatrix * inverse(targetMatrix).
public sealed record CanonicalTransformResolutionResult(
	Matrix4x4? SourceToTargetLocal,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => SourceToTargetLocal.HasValue && Diagnostics.Count == 0;
}

public sealed class CanonicalTransformResolver
{
	public CanonicalTransformResolutionResult TryResolve(
		UnitMeshModel source,
		int sourceMeshInfoIndex,
		UnitMeshModel target,
		int targetMeshInfoIndex)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(target);
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		var sourceMesh = FindMesh(source, sourceMeshInfoIndex, "source", diagnostics);
		var targetMesh = FindMesh(target, targetMeshInfoIndex, "target", diagnostics);
		var sourceMatrix = sourceMesh is null ? (Matrix4x4?)null : TryGetMatrix(source, sourceMesh.TransformIndex, "source", diagnostics);
		var targetMatrix = targetMesh is null ? (Matrix4x4?)null : TryGetMatrix(target, targetMesh.TransformIndex, "target", diagnostics);
		if (sourceMatrix is null || targetMatrix is null)
			return new(null, Array.AsReadOnly(diagnostics.ToArray()));
		if (!Matrix4x4.Invert(targetMatrix.Value, out var inverseTarget))
		{
			diagnostics.Add(new("NonInvertibleTargetTransform", "The target mesh TransformInfo matrix is not invertible."));
			return new(null, Array.AsReadOnly(diagnostics.ToArray()));
		}
		var result = sourceMatrix.Value * inverseTarget;
		if (!IsFinite(result))
		{
			diagnostics.Add(new("InvalidResolvedTransform", "The resolved source-to-target local transform is not finite."));
			return new(null, Array.AsReadOnly(diagnostics.ToArray()));
		}
		return new(result, Array.Empty<CanonicalPlanDiagnostic>());
	}

	private static UnitMeshInfo? FindMesh(UnitMeshModel model, int index, string role, List<CanonicalPlanDiagnostic> diagnostics)
	{
		var matches = model.Meshes.Where(mesh => mesh.Index == index).ToArray();
		if (matches.Length != 1)
		{
			diagnostics.Add(new("MeshInfoNotFound", $"The {role} Unit must contain exactly one MeshInfo {index}; found {matches.Length}."));
			return null;
		}
		return matches[0];
	}

	private static Matrix4x4? TryGetMatrix(UnitMeshModel model, uint index, string role, List<CanonicalPlanDiagnostic> diagnostics)
	{
		if (index >= model.TransformInfo.Matrices.Count)
		{
			diagnostics.Add(new("MissingTransformInfoMatrix", $"The {role} mesh TransformInfo matrix {index} is absent."));
			return null;
		}
		var values = model.TransformInfo.Matrices[(int)index].Values;
		if (values.Count != 16 || values.Any(value => !float.IsFinite(value)))
		{
			diagnostics.Add(new("InvalidTransformInfoMatrix", $"The {role} TransformInfo matrix {index} is not a finite 4x4 matrix."));
			return null;
		}
		return new Matrix4x4(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15]);
	}

	private static bool IsFinite(Matrix4x4 matrix)
		=> typeof(Matrix4x4).GetFields().Where(field => field.FieldType == typeof(float)).Select(field => (float)field.GetValue(matrix)!).All(float.IsFinite);
}
