using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Writes an isolated current-target cross-armor reconstruction candidate from an approved read-only plan.
public interface ICrossArmorTransferCandidateService
{
	ValueTask<CrossArmorTransferCandidateResult> GenerateCandidateAsync(
		CrossArmorTransferCandidateRequest request,
		CancellationToken cancellationToken = default);
}