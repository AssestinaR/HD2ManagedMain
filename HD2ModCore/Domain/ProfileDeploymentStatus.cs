namespace HD2ModCore.Domain;

// Purpose: Immutable deployment-lane snapshot for UI projections and diagnostics.
public sealed record ProfileDeploymentStatus(
	ProfileDeploymentStage Stage,
	ProfileId? ProfileId,
	long TargetRevision,
	DateTimeOffset? BufferStartedUtc,
	DateTimeOffset? BufferEndsUtc,
	string? Message,
	ApplyResult? ApplyResult)
{
	public static ProfileDeploymentStatus Idle { get; } = new(ProfileDeploymentStage.Idle, null, 0, null, null, null, null);
}
