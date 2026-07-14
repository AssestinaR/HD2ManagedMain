namespace HD2ModCore.Domain;

// 作用：用户的一套模组方案；成员身份即启用意图，Revision 标识可部署内容版本。
// Purpose: A user preset whose membership means enabled; Revision identifies deployable content changes.
public sealed record Profile(
	ProfileId Id,
	string Name,
	DateTimeOffset CreatedUtc,
	DateTimeOffset? ModifiedUtc,
	IReadOnlyList<ProfileEntry> Entries,
	long Revision = 0);
