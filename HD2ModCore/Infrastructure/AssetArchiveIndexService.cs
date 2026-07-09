using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

	public async ValueTask<GameDataIndexStatus> GetIndexStatusAsync(
		string gameDataDirectory,
		string archiveHashesJson,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(gameDataDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(gameDataDirectory));
		}

		var stored = await GetFingerprintAsync(cancellationToken).ConfigureAwait(false);
		if (stored is null)
		{
			return new GameDataIndexStatus(GameDataIndexState.Missing, null, Path.GetFullPath(gameDataDirectory), string.Empty);
		}

		ArchiveHashesRoot root;
		try
		{
			root = JsonSerializer.Deserialize<ArchiveHashesRoot>(archiveHashesJson) ?? new ArchiveHashesRoot();
		}
		catch (JsonException)
		{
			return new GameDataIndexStatus(GameDataIndexState.Invalid, stored, Path.GetFullPath(gameDataDirectory), string.Empty);
		}

		var archives = FlattenArchives(root);
		var currentFingerprint = await ComputeGameDataFingerprintAsync(gameDataDirectory, archives, cancellationToken).ConfigureAwait(false);
		var state = string.Equals(stored.SourceFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase)
			? GameDataIndexState.Current
			: GameDataIndexState.Stale;

		return new GameDataIndexStatus(state, stored, Path.GetFullPath(gameDataDirectory), currentFingerprint);
	}

	public async ValueTask<GameDataIndexFingerprint?> GetFingerprintAsync(CancellationToken cancellationToken = default)
	{
		if (!File.Exists(_paths.DbPath))
		{
			return null;
		}

		await using var connection = new SqliteConnection($"Data Source={_paths.DbPath};Mode=ReadOnly;Cache=Shared");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

		var builtUtcText = await SqliteSchema.GetMetaAsync(connection, "built_utc", cancellationToken).ConfigureAwait(false);
		if (!DateTimeOffset.TryParse(builtUtcText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var builtUtc))
		{
			return null;
		}

		return new GameDataIndexFingerprint(
			await SqliteSchema.GetMetaAsync(connection, "game_data_directory", cancellationToken).ConfigureAwait(false) ?? string.Empty,
			builtUtc,
			ParseInt(await SqliteSchema.GetMetaAsync(connection, "archives_total", cancellationToken).ConfigureAwait(false)),
			ParseInt(await SqliteSchema.GetMetaAsync(connection, "archives_indexed", cancellationToken).ConfigureAwait(false)),
			ParseInt(await SqliteSchema.GetMetaAsync(connection, "asset_keys_total", cancellationToken).ConfigureAwait(false)),
			await SqliteSchema.GetMetaAsync(connection, "source_fingerprint", cancellationToken).ConfigureAwait(false) ?? string.Empty);
	}

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


		SqliteConnection.ClearAllPools();
		NormalizeExistingIndexFileAttributes();

		var archives = FlattenArchives(root);

		await using var connection = new SqliteConnection($"Data Source={_paths.DbPath};Pooling=False");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken).ConfigureAwait(false);

		await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		await SqliteSchema.ClearIndexDataAsync(connection, cancellationToken).ConfigureAwait(false);

		var resolver = new GameDataPackageResolver(gameDataDirectory);
		var total = archives.Count;
		var current = 0;
		var indexed = 0;
		var sourceFingerprint = await ComputeGameDataFingerprintAsync(gameDataDirectory, archives, cancellationToken).ConfigureAwait(false);

		var df = new Dictionary<AssetKey, int>();
		var assetArchives = new Dictionary<AssetKey, HashSet<string>>();

		foreach (var (archiveId, category, displayName) in archives)
		{
			cancellationToken.ThrowIfCancellationRequested();
			current++;
			progress?.Report(new IndexBuildProgress(current, total, archiveId));

			await InsertArchiveAsync(connection, archiveId, category, displayName, cancellationToken).ConfigureAwait(false);

			IReadOnlySet<AssetKey> keys;
			try
			{
				var toc = await resolver.GetPackageTocAsync(archiveId, cancellationToken).ConfigureAwait(false);
				if (toc is null)
				{
					continue;
				}

				keys = _tocScanner.ScanAssetKeys(toc.Data, toc.UsesSlimEntryOffset);
			}
			catch
			{
				continue;
			}

			indexed++;

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
		await SqliteSchema.SetMetaAsync(connection, "archives_indexed", indexed.ToString(), cancellationToken).ConfigureAwait(false);
		await SqliteSchema.SetMetaAsync(connection, "asset_keys_total", df.Count.ToString(), cancellationToken).ConfigureAwait(false);
		await SqliteSchema.SetMetaAsync(connection, "game_data_directory", Path.GetFullPath(gameDataDirectory), cancellationToken).ConfigureAwait(false);
		await SqliteSchema.SetMetaAsync(connection, "source_fingerprint", sourceFingerprint, cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<IReadOnlyList<AssetArchiveMatch>> FindAssetArchivesAsync(
		IReadOnlySet<AssetKey> assetKeys,
		CancellationToken cancellationToken = default)
	{
		if (assetKeys is null)
		{
			throw new ArgumentNullException(nameof(assetKeys));
		}

		if (assetKeys.Count == 0 || !File.Exists(_paths.DbPath))
		{
			return Array.Empty<AssetArchiveMatch>();
		}

		await using var connection = new SqliteConnection($"Data Source={_paths.DbPath};Mode=ReadOnly;Cache=Shared");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

		var matches = new List<AssetArchiveMatch>();
		foreach (var key in assetKeys.OrderBy(x => x.TypeId).ThenBy(x => x.FileId))
		{
			cancellationToken.ThrowIfCancellationRequested();
			matches.Add(new AssetArchiveMatch(key, await GetArchiveMetadataForAssetAsync(connection, key, cancellationToken).ConfigureAwait(false)));
		}

		return matches;
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

		if (assetKeys.Count == 0 || !File.Exists(_paths.DbPath))
		{
			return new Dictionary<string, int>();
		}

		await using var connection = new SqliteConnection($"Data Source={_paths.DbPath};Mode=ReadOnly;Cache=Shared");
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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

	private void NormalizeExistingIndexFileAttributes()
	{
		foreach (var path in new[] { _paths.DbPath, _paths.DbPath + "-wal", _paths.DbPath + "-shm" })
		{
			if (!File.Exists(path))
			{
				continue;
			}

			File.SetAttributes(path, FileAttributes.Normal);
		}
	}

	private static List<(string ArchiveId, string Category, string DisplayName)> FlattenArchives(ArchiveHashesRoot root)
	{
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

		return archives;
	}

	private static async ValueTask<string> ComputeGameDataFingerprintAsync(string gameDataDirectory, IReadOnlyList<(string ArchiveId, string Category, string DisplayName)> archives, CancellationToken cancellationToken)
	{
		var resolver = new GameDataPackageResolver(gameDataDirectory);
		var builder = new StringBuilder();
		builder.Append(Path.GetFullPath(gameDataDirectory)).AppendLine();
		foreach (var archive in archives.OrderBy(x => x.ArchiveId, StringComparer.OrdinalIgnoreCase))
		{
			builder.Append(archive.ArchiveId).Append('|').Append(archive.Category).Append('|').Append(archive.DisplayName).Append('|');
			builder.Append(await resolver.GetPackageFingerprintAsync(archive.ArchiveId, cancellationToken).ConfigureAwait(false));
			builder.AppendLine();
		}

		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}

	private static int ParseInt(string? value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

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

	private static async Task<IReadOnlyList<ArchiveMetadata>> GetArchiveMetadataForAssetAsync(SqliteConnection connection, AssetKey key, CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = @"
SELECT a.archive_id, a.category, a.display_name
FROM asset_archives aa
JOIN archives a ON a.archive_id = aa.archive_id
WHERE aa.type_id=$t AND aa.file_id=$f
ORDER BY a.category, a.display_name, a.archive_id";
		cmd.Parameters.AddWithValue("$t", unchecked((long)key.TypeId));
		cmd.Parameters.AddWithValue("$f", unchecked((long)key.FileId));

		var archives = new List<ArchiveMetadata>();
		await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			archives.Add(new ArchiveMetadata(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
		}

		return archives;
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
