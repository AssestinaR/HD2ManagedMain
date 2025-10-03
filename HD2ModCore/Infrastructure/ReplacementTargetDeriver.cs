using HD2ModCore.Application;
using HD2ModCore.Domain;
using Microsoft.Data.Sqlite;

namespace HD2ModCore.Infrastructure;

// 作用：基于 SQLite 索引投票结果，生成可直接展示的替换目标列表（TopN + 其余候选）。
// Purpose: Builds a UI-friendly replacement target list (TopN + others) from SQLite index votes.
public sealed class ReplacementTargetDeriver : IReplacementTargetDeriver
{
	private readonly StoragePaths _paths;
	private readonly IAssetArchiveIndexService _indexService;

	public ReplacementTargetDeriver(StoragePaths paths, IAssetArchiveIndexService indexService)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
	}

	public async ValueTask<ReplacementTargetsResult> DeriveAsync(
		IReadOnlySet<AssetKey> assetKeys,
		IndexFilterSettings filterSettings,
		int topN = 5,
		CancellationToken cancellationToken = default)
	{
		if (topN <= 0)
		{
			topN = 5;
		}

		var votes = await _indexService.VoteArchivesAsync(assetKeys, filterSettings, cancellationToken).ConfigureAwait(false);
		if (votes.Count == 0)
		{
			return new ReplacementTargetsResult(Array.Empty<ArchiveVote>(), Array.Empty<ArchiveVote>());
		}

		var archiveInfos = await GetArchiveInfosAsync(votes.Keys, cancellationToken).ConfigureAwait(false);

		var ordered = votes
			.Select(kvp =>
			{
				archiveInfos.TryGetValue(kvp.Key, out var info);
				var category = info?.Category ?? string.Empty;
				var displayName = info?.DisplayName ?? kvp.Key;
				return new ArchiveVote(kvp.Key, category, displayName, kvp.Value);
			})
			.OrderByDescending(v => v.Votes)
			.ThenBy(v => v.Category, StringComparer.OrdinalIgnoreCase)
			.ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var top = ordered.Take(topN).ToList();
		var others = ordered.Skip(topN).ToList();
		return new ReplacementTargetsResult(top, others);
	}

	private async ValueTask<Dictionary<string, ArchiveInfo>> GetArchiveInfosAsync(IEnumerable<string> archiveIds, CancellationToken cancellationToken)
	{
		var result = new Dictionary<string, ArchiveInfo>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(_paths.DbPath))
		{
			return result;
		}

		await using var connection = new SqliteConnection($"Data Source={_paths.DbPath};Mode=ReadOnly;Cache=Shared");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

		await using var cmd = connection.CreateCommand();
		var ids = archiveIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (ids.Count == 0)
		{
			return result;
		}

		var parameters = new List<string>(ids.Count);
		for (var i = 0; i < ids.Count; i++)
		{
			var p = "$a" + i;
			parameters.Add(p);
			cmd.Parameters.AddWithValue(p, ids[i]);
		}

		cmd.CommandText = $"SELECT archive_id, category, display_name FROM archives WHERE archive_id IN ({string.Join(",", parameters)})";

		await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var id = reader.GetString(0);
			var category = reader.GetString(1);
			var name = reader.GetString(2);
			result[id] = new ArchiveInfo(category, name);
		}

		return result;
	}

	private sealed record ArchiveInfo(string Category, string DisplayName);
}
