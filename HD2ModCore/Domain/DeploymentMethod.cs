namespace HD2ModCore.Domain;

// 作用：实际使用的部署方式。
// Purpose: Actual deployment method used for a file.
public enum DeploymentMethod
{
	HardLink,
	SymbolicLink,
	Copy,
	Delete,
	StateFile,
}