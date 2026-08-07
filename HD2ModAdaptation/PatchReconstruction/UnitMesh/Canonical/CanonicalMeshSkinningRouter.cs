using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Routes Canonical source meshes through proxy, skinned, or static preparation without mixing workflow policy.
public sealed record CanonicalMeshSkinningRouteResult(
	UnitRawMeshData? Mesh,
	UnitBoneInfo? ProvisionalBoneInfo,
	bool IsProxy,
	bool ParticipatesInLodPalette,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Mesh is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalMeshSkinningRouter
{
	private readonly CanonicalBoneRebuilder boneRebuilder;
	private readonly CanonicalStaticMeshBinder staticMeshBinder;

	public CanonicalMeshSkinningRouter(
		CanonicalBoneRebuilder? boneRebuilder = null,
		CanonicalStaticMeshBinder? staticMeshBinder = null)
	{
		this.boneRebuilder = boneRebuilder ?? new CanonicalBoneRebuilder();
		this.staticMeshBinder = staticMeshBinder ?? new CanonicalStaticMeshBinder();
	}

	public CanonicalMeshSkinningRouteResult TryPrepare(
		UnitMeshModel source,
		UnitRawMeshData sourceMesh,
		UnitMeshModel target,
		UnitRawMeshData targetMesh,
		UnitStreamInfo targetStream,
		CanonicalSkinningMode skinningMode = CanonicalSkinningMode.PreserveSourceWeights,
		CanonicalBoneAnchor? boneAnchor = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(sourceMesh);
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(targetMesh);
		ArgumentNullException.ThrowIfNull(targetStream);

		var isProxy = targetMesh.LodIndex == -1;
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		var targetIsSkinned = UsesSkinningStream(targetStream);
		var sourceIsSkinned = HasBoneData(source, sourceMesh);

		if (isProxy)
		{
			// LodIndex=-1 has no BoneInfo identity. Static proxy streams must not
			// inherit Type=6/7 from a skinned source or reach palette compilation.
			if (!targetIsSkinned)
			{
				return new(RemoveSkinningComponents(sourceMesh), null, true, false, Array.Empty<CanonicalPlanDiagnostic>());
			}

			// A skinned proxy cannot be represented by the ordinary BoneInfo path.
			// Preserve an already skinned source only as an explicit proxy result;
			// static geometry cannot be silently encoded into this stream.
			if (!sourceIsSkinned)
				diagnostics.Add(new("ProxyStaticSkinningUnsupported", "A LodIndex=-1 proxy uses a skinned stream but the source mesh has no skinning data."));
			return new(sourceMesh, null, true, false, Array.AsReadOnly(diagnostics.ToArray()));
		}

		if (sourceIsSkinned)
		{
			var rebuilt = boneRebuilder.TryRebuild(source, sourceMesh, target, targetMesh);
			return new(rebuilt.Mesh, rebuilt.BoneInfo, false, rebuilt.IsValid, rebuilt.Diagnostics);
		}

		if (!targetIsSkinned)
			return new(sourceMesh, null, false, false, Array.Empty<CanonicalPlanDiagnostic>());

		if (skinningMode is not (CanonicalSkinningMode.BindStaticToTargetMeshTransform or CanonicalSkinningMode.BindStaticToAvatarBone))
		{
			diagnostics.Add(new("CanonicalStaticAnchorRequired", "Static source geometry written to a skinned target must declare an explicit Canonical anchor policy."));
			return new(null, null, false, false, Array.AsReadOnly(diagnostics.ToArray()));
		}

		var bound = staticMeshBinder.TryBind(
			target,
			targetMesh,
			sourceMesh,
			targetStream,
			skinningMode == CanonicalSkinningMode.BindStaticToAvatarBone ? boneAnchor : CanonicalBoneAnchor.TargetMeshTransform);
		return new(bound.Mesh, bound.BoneInfo, false, bound.IsValid, bound.Diagnostics);
	}

	private static bool HasBoneData(UnitMeshModel model, UnitRawMeshData mesh)
		=> mesh.LodIndex >= 0 && mesh.LodIndex < model.BoneInfos.Count && model.BoneInfos[mesh.LodIndex].RealIndices.Count > 0;

	private static bool UsesSkinningStream(UnitStreamInfo stream)
		=> stream.Components.Any(component => component.Type == 6) && stream.Components.Any(component => component.Type == 7);

	private static UnitRawMeshData RemoveSkinningComponents(UnitRawMeshData mesh)
		=> mesh with
		{
			Vertices = mesh.Vertices.Select(vertex => vertex with
			{
				Components = vertex.Components.Where(component => component.Type is not (6 or 7)).ToArray(),
				Data = Array.Empty<byte>()
			}).ToArray()
		};
}