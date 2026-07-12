namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Starts the SDK-style reconstruction path by binding explicit inputs to SDK/autofix resource semantics.
public sealed class SdkStyleUnitReconstructionPipeline
{
	private const int StateMachineReferenceOffset = 32;

	public SdkStyleUnitReconstructionPlan CreatePlan(
		SdkStyleReconstructionRequest request,
		GameDataUnitMesh targetShell,
		SdkStyleAvatarRigResource avatarRig,
		IReadOnlyCollection<PatchUnitMesh> sourceUnits)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(targetShell);
		ArgumentNullException.ThrowIfNull(avatarRig);
		ArgumentNullException.ThrowIfNull(sourceUnits);
		request.Validate();
		ValidateUnitIdentity(targetShell.AssetKey, request.TargetUnitAssetKey, "target shell");
		ValidateUnitIdentity(avatarRig.AssetKey, request.ResolvedAvatarUnitAssetKey, "avatar rig");

		var sourceByKey = sourceUnits.ToDictionary(unit => unit.Entry.AssetKey);
		var targetIndexes = new HashSet<int>();
		var bindings = new List<SdkStyleMeshBinding>();
		foreach (var mapping in request.MeshMappings)
		{
			if (!targetIndexes.Add(mapping.TargetMeshInfoIndex))
			{
				throw new InvalidDataException($"Target mesh {mapping.TargetMeshInfoIndex} has more than one explicit source mapping.");
			}
			if (!sourceByKey.TryGetValue(mapping.SourceUnitAssetKey, out var sourceUnit))
			{
				throw new KeyNotFoundException($"Source Unit 0x{mapping.SourceUnitAssetKey.FileId:x16} was not supplied for the explicit mapping.");
			}

			var sourceMesh = GetMesh(sourceUnit.Model, mapping.SourceMeshInfoIndex, "source");
			var targetMesh = GetMesh(targetShell.Model, mapping.TargetMeshInfoIndex, "target");
			bindings.Add(new SdkStyleMeshBinding(
				sourceUnit,
				sourceMesh.Index,
				targetMesh.Index,
				targetMesh.LodIndex,
				targetMesh.MaterialSlotIds));
		}

		var resources = new SdkStyleResourcePlan(
			targetShell.AssetKey,
			avatarRig.AssetKey,
			avatarRig.BonesReference,
			avatarRig.StateMachineReference,
			SdkStyleAvatarRigConstants.AvatarRigObjectName,
			SdkStyleAvatarRigConstants.AvatarMeshNamePrefix);
		return new SdkStyleUnitReconstructionPlan(targetShell, avatarRig, bindings, resources, request.DependencyPolicy);
	}

	private static void ValidateUnitIdentity(AssetKey actual, AssetKey expected, string name)
	{
		if (actual != expected)
		{
			throw new InvalidDataException($"The supplied {name} Unit does not match the requested asset key.");
		}
	}

	private static UnitMeshInfo GetMesh(UnitMeshModel model, int meshInfoIndex, string role)
	{
		var mesh = model.Meshes.SingleOrDefault(candidate => candidate.Index == meshInfoIndex);
		return mesh ?? throw new KeyNotFoundException($"The {role} mesh index {meshInfoIndex} was not found.");
	}

	private static ulong ReadReference(ReadOnlySpan<byte> tocData, int offset, string name)
	{
		if (tocData.Length < offset + sizeof(ulong))
		{
			throw new InvalidDataException($"Unit TocData is too short to read its {name} reference.");
		}
		return BitConverter.ToUInt64(tocData.Slice(offset, sizeof(ulong)));
	}
}