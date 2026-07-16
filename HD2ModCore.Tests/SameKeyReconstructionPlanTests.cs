using HD2ModCore.Domain;

namespace HD2ModCore.Tests;

// Purpose: Verifies same-key plan eligibility remains conservative before any reconstruction writer is enabled.
public sealed class SameKeyReconstructionPlanTests
{
	[Fact]
	public void IsGeometryEligible_ExperimentalCandidate_IsFalse()
	{
		var sourceEntry = new PatchTocEntry(new AssetKey(1, 2), "source.patch", "source.patch");
		var candidate = new UnitMeshReplacementCandidate(0, 0, 1, 2, "target", "source", 0, 0, 12, Array.Empty<UnitMeshReplacementComponentSignature>(), UnitMeshReplacementCandidateKind.ExperimentalFallback, 1, "fallback");
		var adaptation = new UnitMeshAdaptationPlan(
			new UnitMeshAdaptationIntent(sourceEntry, new ArchiveTocEntry(sourceEntry.AssetKey, "target"), null),
			CanWrite: true,
			new[] { candidate },
			new[] { new UnitMeshAdaptationStep(UnitMeshAdaptationStepKind.ReplaceWithSource, 0, 0, "fallback", candidate) },
			EditedModel: null,
			WriteResult: null,
			Reason: "writable");
		var plan = new SameKeyUnitReconstructionPlan(sourceEntry.AssetKey, sourceEntry, new ArchiveMetadata("target", "Armor", "Target"), new[] { new ArchiveMetadata("target", "Armor", "Target") }, adaptation, Array.Empty<CoreIssue>());

		Assert.False(plan.IsGeometryEligible);
	}
}