using HD2ModCore.Domain;

namespace HD2ModCore.Tests;

// Purpose: Verifies the same-key UI state permits writing only with a current index, a source patch, a plan, and no errors.
public sealed class ModSameKeyReconstructionStateTests
{
	[Fact]
	public void CanWrite_CompletePlanWithoutErrors_IsTrue()
	{
		var state = new ModSameKeyReconstructionState(
			new ModNodeId(Guid.NewGuid()),
			"source.patch",
			new SameKeyReconstructionPlan(new SameKeyReconstructionRequest("source.patch", "game-data"), Array.Empty<SameKeyUnitReconstructionPlan>(), Array.Empty<CoreIssue>()),
			true,
			0,
			0,
			0,
			0,
			0,
			Array.Empty<CoreIssue>());

		Assert.False(state.CanWrite);
	}

	[Fact]
	public void CanWrite_Error_IsFalse()
	{
		var unitEntry = new PatchTocEntry(new AssetKey(1, 2), "source.patch", "source.patch");
		var unit = new SameKeyUnitReconstructionPlan(unitEntry.AssetKey, unitEntry, null, Array.Empty<ArchiveMetadata>(), null, Array.Empty<CoreIssue>());
		var state = new ModSameKeyReconstructionState(
			new ModNodeId(Guid.NewGuid()),
			"source.patch",
			new SameKeyReconstructionPlan(new SameKeyReconstructionRequest("source.patch", "game-data"), new[] { unit }, Array.Empty<CoreIssue>()),
			true,
			0,
			1,
			0,
			1,
			0,
			new[] { new CoreIssue(CoreIssueSeverity.Error, "Blocked", "blocked") });

		Assert.False(state.CanWrite);
	}
}
