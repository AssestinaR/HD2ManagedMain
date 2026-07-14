namespace HD2ModCore.Domain;

// Purpose: Observable state of the sole serialized profile deployment lane.
public enum ProfileDeploymentStage
{
	Idle,
	Deploying,
	Deactivating,
	Completed,
	Failed,
	Canceled,
}
