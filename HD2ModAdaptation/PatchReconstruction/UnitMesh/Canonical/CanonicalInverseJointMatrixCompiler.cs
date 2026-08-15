using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Shared SDK-compatible inverse-joint generation. Bone matrices must be rebuilt whenever a
// palette introduces transforms that did not exist in the target Unit's original BoneInfo.
internal static class CanonicalInverseJointMatrixCompiler
{
    public static IReadOnlyList<byte[]> Build(UnitMeshModel target, uint meshTransformIndex, IReadOnlyList<int> realIndices, List<CanonicalPlanDiagnostic> diagnostics)
    {
        if (meshTransformIndex >= target.TransformInfo.Matrices.Count)
        {
            diagnostics.Add(new("MissingMeshTransform", "The target mesh TransformInfo matrix is absent."));
            return [];
        }
        var mesh = ToMatrix(target.TransformInfo.Matrices[(int)meshTransformIndex], diagnostics);
        if (mesh is null || !Matrix4x4.Invert(Matrix4x4.Transpose(mesh.Value), out var origin))
        {
            diagnostics.Add(new("NonInvertibleMeshTransform", "The target mesh transform is not invertible."));
            return [];
        }
        var result = new List<byte[]>(realIndices.Count);
        foreach (var index in realIndices)
        {
            if (index < 0 || index >= target.TransformInfo.Matrices.Count)
            {
                diagnostics.Add(new("MissingBoneTransform", "A target bone TransformInfo matrix is absent."));
                continue;
            }
            var bone = ToMatrix(target.TransformInfo.Matrices[index], diagnostics);
            if (bone is null || !Matrix4x4.Invert(origin * Matrix4x4.Transpose(bone.Value), out var inverse))
            {
                diagnostics.Add(new("NonInvertibleBoneTransform", "A target inverse-joint matrix cannot be generated safely."));
                continue;
            }
            var raw = Matrix4x4.Transpose(inverse);
            result.Add([.. new[] { raw.M11, raw.M12, raw.M13, raw.M14, raw.M21, raw.M22, raw.M23, raw.M24, raw.M31, raw.M32, raw.M33, raw.M34, raw.M41, raw.M42, raw.M43, raw.M44 }.SelectMany(BitConverter.GetBytes)]);
        }
        return result;
    }

    private static Matrix4x4? ToMatrix(UnitTransformMatrix matrix, List<CanonicalPlanDiagnostic> diagnostics)
    {
        if (matrix.Values.Count != 16 || matrix.Values.Any(value => !float.IsFinite(value)))
        {
            diagnostics.Add(new("InvalidTransformInfoMatrix", "TransformInfo matrix is not a finite 4x4 matrix."));
            return null;
        }
        var v = matrix.Values;
        return new Matrix4x4(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
    }
}
