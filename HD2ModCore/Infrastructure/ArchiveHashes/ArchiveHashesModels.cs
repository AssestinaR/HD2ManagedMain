namespace HD2ModCore.Infrastructure.ArchiveHashes;

// 作用：用于解析 archivehashes.json 的 DTO（category -> (archiveId -> displayName)）。
// Purpose: DTOs for parsing archivehashes.json (category -> (archiveId -> displayName)).
internal sealed class ArchiveHashesRoot : Dictionary<string, Dictionary<string, string>>
{
}
