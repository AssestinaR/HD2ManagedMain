namespace HD2ModCore.Domain;

// 作用：Profile（配置方案）的稳定标识。
// Purpose: Stable identifier for a Profile (configuration/preset).
public readonly record struct ProfileId(Guid Value)
{
	public static ProfileId New() => new(Guid.NewGuid());
}
