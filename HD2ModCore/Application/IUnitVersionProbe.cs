using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

// Purpose: Reads only the Unit version field from already-discovered Patch entries.
namespace HD2ModCore.Application;

public interface IUnitVersionProbe
{
	ValueTask<IReadOnlyList<UnitVersionEvidence>> ProbeAsync(
	PatchGroupAnalysis analysis,
	CancellationToken cancellationToken = default);
}