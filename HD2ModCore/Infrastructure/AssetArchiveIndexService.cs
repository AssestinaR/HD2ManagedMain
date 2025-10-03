using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure.ArchiveHashes;
using HD2ModCore.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace HD2ModCore.Infrastructure;

// 作用：使用 SQLite 构建并查询持久化的 (TypeID, FileID) -> ArchiveId 反向索引，用于推导替换目标与冲突分析。
// Purpose: Builds and queries a persisted (TypeID, FileID) -> ArchiveId reverse index using SQLite.
public sealed class AssetArchiveIndexService : IAssetArchiveIndexService
{
	private readonly StoragePaths _paths;
	private readonly IPatchTocScanner _tocScanner;

	public AssetArchiveIndexService(StoragePaths paths, IPatchTocScanner tocScanner)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_tocScanner = tocScanner ?? throw new ArgumentNullException(nameof(tocScanner));
	}

	public ValueTask<bool> IndexExistsAsync(CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(File.Exists(_paths.DbPath));

	public async ValueTask BuildOrRebuildAsync(
		string gameDataDirectory,
		string archiveHashesJson,
		IProgress<IndexBuildProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(gameDataDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameDataDirectory));
		}

		Directory.CreateDirectory(_paths.IndexDirectory);

		ArchiveHashesRoot root;
		try
		{
			root = JsonSerializer.Deserialize<ArchiveHashesRoot>(archiveHashesJson) ?? new ArchiveHashesRoot();
		}
		catch (JsonException ex)
		{
			throw new FormatException("Invalid archivehashes.json format.", ex);
		}

		var archives = new List<(string ArchiveId, string Category, string DisplayName)>();
		foreach (var (category, map) in root)
		{
			foreach (var (archiveId, displayName) in map)
			{
				if (string.IsNullOrWhiteSpace(archiveId))
				{
					continue;
				}
				archives.Add((archiveId.Trim().ToLowerInvariant(), category, displayName));
			}
		}

		await using var connection = new SqliteConnection($"Data Source={_paths.DbPath};Cache=Shared");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken).ConfigureAwait(false);

		await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		await SqliteSchema.ClearIndexDataAsync(connection, cancellationToken).ConfigureAwait(false);

		var total = archives.Count;
		var current = 0;

		var df = new Dictionary<AssetKey, int>();
		var assetArchives = new Dictionary<AssetKey, HashSet<string>>();

		foreach (var (archiveId, category, displayName) in archives)
		{
			cancellationToken.ThrowIfCancellationRequested();
			current++;
			progress?.Report(new IndexBuildProgress(current, total, archiveId));

			await InsertArchiveAsync(connection, archiveId, category, displayName, cancellationToken).ConfigureAwait(false);

			var tocPath = Path.Combine(gameDataDirectory, archiveId);
			if (!File.Exists(tocPath))
			{
				continue;
			}

			IReadOnlySet<AssetKey> keys;
			try
			{
				keys = await _tocScanner.ScanAssetKeysAsync(tocPath, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				continue;
			}

			foreach (var key in keys)
			{
				if (df.TryGetValue(key, out var count))
				{
					df[key] = count + 1;
				}
				else
				{
					df[key] = 1;
				}

				if (!assetArchives.TryGetValue(key, out var set))
				{
					set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					assetArchives[key] = set;
				}
				set.Add(archiveId);
			}
		}

		foreach (var (key, count) in df)
		{
			await InsertAssetAsync(connection, key, count, cancellationToken).ConfigureAwait(false);
		}

		foreach (var (key, archivesSet) in assetArchives)
		{
			foreach (var archiveId in archivesSet)
			{
				await InsertAssetArchiveAsync(connection, key, archiveId, cancellationToken).ConfigureAwait(false);
			}
		}

		await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

		await SqliteSchema.SetMetaAsync(connection, "built_utc", DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
		await SqliteSchema.SetMetaAsync(connection, "archives_total", total.ToString(), cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<IReadOnlyDictionary<string, int>> VoteArchivesAsync(
		IReadOnlySet<AssetKey> assetKeys,
		IndexFilterSettings filterSettings,
		CancellationToken cancellationToken = default)
	{
		if (assetKeys is null)
		{
			throw new ArgumentNullException(nameof(assetKeys));
		}

		if (assetKeys.Count == 0)
		{
			return new Dictionary<string, int>();
		}

		await using var connection = new SqliteConnection($"Data Source={_paths.DbPath};Mode=ReadOnly;Cache=Shared");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken).ConfigureAwait(false);

		var totalArchivesText = await SqliteSchema.GetMetaAsync(connection, "archives_total", cancellationToken).ConfigureAwait(false);
		_ = int.TryParse(totalArchivesText, out var totalArchives);
		if (totalArchives <= 0)
		{
			totalArchives = 1;
		}

		var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var key in assetKeys)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var df = await GetDfAsync(connection, key, cancellationToken).ConfigureAwait(false);
			if (IsFiltered(df, totalArchives, filterSettings))
			{
				continue;
			}

			await foreach (var archiveId in GetArchivesForAssetAsync(connection, key, cancellationToken).ConfigureAwait(false))
			{
				votes.TryGetValue(archiveId, out var count);
				votes[archiveId] = count + 1;
			}
		}

		return votes;
	}

	private static bool IsFiltered(int df, int totalArchives, IndexFilterSettings settings)
	{
		if (df <= 0)
		{
			return true;
		}

		return settings.Mode switch
		{
			IndexFilterMode.AbsoluteCount => settings.AbsoluteThreshold is int t && df > t,
			IndexFilterMode.Percentage => settings.PercentageThreshold is double p && p > 0 && (df / (double)totalArchives) > p,
			_ => false,
		};
	}

	private static async Task InsertArchiveAsync(SqliteConnection connection, string archiveId, string category, string displayName, CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = "INSERT OR REPLACE INTO archives(archive_id,category,display_name) VALUES($a,$c,$n)";
		cmd.Parameters.AddWithValue("$a", archiveId);
		cmd.Parameters.AddWithValue("$c", category);
		cmd.Parameters.AddWithValue("$n", displayName);
		await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	private static async Task InsertAssetAsync(SqliteConnection connection, AssetKey key, int df, CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = "INSERT OR REPLACE INTO assets(type_id,file_id,df) VALUES($t,$f,$d)";
		cmd.Parameters.AddWithValue("$t", unchecked((long)key.TypeId));
		cmd.Parameters.AddWithValue("$f", unchecked((long)key.FileId));
		cmd.Parameters.AddWithValue("$d", df);
		await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	private static async Task InsertAssetArchiveAsync(SqliteConnection connection, AssetKey key, string archiveId, CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = "INSERT OR IGNORE INTO asset_archives(type_id,file_id,archive_id) VALUES($t,$f,$a)";
		cmd.Parameters.AddWithValue("$t", unchecked((long)key.TypeId));
		cmd.Parameters.AddWithValue("$f", unchecked((long)key.FileId));
		cmd.Parameters.AddWithValue("$a", archiveId);
		await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	private static async Task<int> GetDfAsync(SqliteConnection connection, AssetKey key, CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = "SELECT df FROM assets WHERE type_id=$t AND file_id=$f";
		cmd.Parameters.AddWithValue("$t", unchecked((long)key.TypeId));
		cmd.Parameters.AddWithValue("$f", unchecked((long)key.FileId));

		var obj = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
		return obj is null ? 0 : Convert.ToInt32(obj);
	}

	private static async IAsyncEnumerable<string> GetArchivesForAssetAsync(SqliteConnection connection, AssetKey key, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = "SELECT archive_id FROM asset_archives WHERE type_id=$t AND file_id=$f";
		cmd.Parameters.AddWithValue("$t", unchecked((long)key.TypeId));
		cmd.Parameters.AddWithValue("$f", unchecked((long)key.FileId));

		await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			yield return reader.GetString(0);
		}
	}
}
