using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Adapts persisted current-game stream ABI facts into the reconstruction-time layout guard.
internal sealed class CurrentGameStreamLayoutRegistry : ICurrentGameStreamLayoutRegistry
{
	private readonly IReadOnlyList<GameDataStreamLayoutFact> layouts;

	public CurrentGameStreamLayoutRegistry(IEnumerable<GameDataStreamLayoutFact> layouts)
	{
		ArgumentNullException.ThrowIfNull(layouts);
		this.layouts = layouts.ToArray();
	}

	public bool TryResolveCanonicalSkinningLayout(UnitStreamInfo targetStream, IReadOnlyCollection<UnitStreamComponentInfo> requiredSourceComponents, int requiredSkinningCapacity, out UnitStreamInfo layout)
	{
		if (requiredSkinningCapacity is < 1 or > 4)
		{
			layout = default!;
			return false;
		}
		var targetNonSkinning = targetStream.Components
			.Where(component => component.Type is not 6 and not 7)
			.Concat(requiredSourceComponents.Where(component => component.Type is not 6 and not 7))
			.GroupBy(component => (component.Type, component.Index))
			.Select(group => group.OrderByDescending(component => component.Size).ThenByDescending(component => component.Format).First())
			.OrderBy(component => ComponentOrder(component.Type)).ThenBy(component => component.Index);
		var requiredSignature = Signature(targetNonSkinning);
		var match = layouts.Where(candidate =>
			Signature(candidate.Components.Where(component => component.Type is not 6 and not 7)) == requiredSignature
			&& candidate.Components.Count(component => component.Type == 7 && component.Format == 35 && component.Index == 0) == 1
			&& candidate.Components.Count(component => component.Type == 6 && component.Format == 28 && component.Index == 0) == 1
			&& candidate.Components.Count(component => component.Type == 7) == 1
			&& candidate.Components.Count(component => component.Type == 6) == 1)
			.GroupBy(candidate => CreateLayoutKey(candidate))
			.OrderByDescending(group => group.Count())
			.ThenBy(group => group.Key, StringComparer.Ordinal)
			.Select(group => group.OrderBy(candidate => candidate.ArchiveId, StringComparer.Ordinal).ThenBy(candidate => candidate.UnitAssetKey.FileId).ThenBy(candidate => candidate.StreamIndex).First())
			.FirstOrDefault();
		if (match is not null)
		{
			layout = targetStream with
			{
				ComponentInfoId = match.ComponentInfoId,
				NumComponents = checked((ulong)match.Components.Count),
				VertexStride = match.VertexStride,
				Components = match.Components.Select(component => new UnitStreamComponentInfo(component.Type, TypeName(component.Type), component.Format, FormatName(component.Format), component.Index, component.Unknown, component.Size)).ToArray()
			};
			return true;
		}
		layout = default!;
		return false;
	}

	private static string CreateLayoutKey(GameDataStreamLayoutFact layout)
		=> $"{layout.VertexStride}|{layout.LayoutSignature}";

	private static string Signature(IEnumerable<GameDataStreamComponentFact> components)
		=> string.Join(";", components.Select(component => $"{component.Type}:{component.Format}:{component.Index}:{component.Unknown:x16}:{component.Size}"));

	private static string Signature(IEnumerable<UnitStreamComponentInfo> components)
		=> string.Join(";", components.Select(component => $"{component.Type}:{component.Format}:{component.Index}:{component.Unknown:x16}:{component.Size}"));

	private static int ComponentOrder(uint type) => type switch { 5 => 0, 0 => 1, 1 => 2, 2 => 3, 3 => 4, 4 => 5, _ => 6 };

	private static string TypeName(uint type) => type switch
	{
		0 => "position", 1 => "normal", 2 => "tangent", 3 => "bitangent", 4 => "uv", 5 => "color", 6 => "bone_index", 7 => "bone_weight", _ => "unknown"
	};

	private static string FormatName(uint format) => format switch
	{
		0 => "float", 1 => "vec2_float", 2 => "vec3_float", 3 => "vec4_float", 4 => "rgba_r8g8b8a8", 24 => "vec4_uint32", 28 => "vec4_uint8", 29 => "vec4_1010102", 30 => "unk_normal", 33 => "vec2_half", 35 => "vec4_half", _ => "unknown"
	};
}