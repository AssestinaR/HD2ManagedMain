using HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Generates a current-target hidden Unit mesh without applying armor-specific mapping or skeleton policy.
public interface IHiddenUnitGenerator
{
	CanonicalPlaceholderMinificationResult Generate(UnitRawMeshData target, UnitStreamInfo targetStream);
}

public sealed class HiddenUnitGenerator : IHiddenUnitGenerator
{
	private readonly CanonicalPlaceholderMinifier minifier;

	public HiddenUnitGenerator(CanonicalPlaceholderMinifier? minifier = null)
	{
		this.minifier = minifier ?? new CanonicalPlaceholderMinifier();
	}

	public CanonicalPlaceholderMinificationResult Generate(UnitRawMeshData target, UnitStreamInfo targetStream)

		=> minifier.TryMinify(target, targetStream);
}