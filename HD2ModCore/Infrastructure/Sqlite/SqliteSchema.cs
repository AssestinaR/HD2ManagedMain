using Microsoft.Data.Sqlite;

namespace HD2ModCore.Infrastructure.Sqlite;

// 作用：定义并创建用于“资产键 -> 原版 archive”反向索引的 SQLite 数据库结构。
// Purpose: Defines and applies the SQLite schema used for the asset->archive reverse index.
internal static class SqliteSchema
{
	public const int SchemaVersion = 1;

	public static async Task EnsureCreatedAsync(SqliteConnection connection, CancellationToken cancellationToken)
	{
		await using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;

CREATE TABLE IF NOT EXISTS meta (
	key TEXT PRIMARY KEY,
	value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS archives (
	archive_id TEXT PRIMARY KEY,
	category TEXT NOT NULL,
	display_name TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS assets (
	type_id INTEGER NOT NULL,
	file_id INTEGER NOT NULL,
	df INTEGER NOT NULL,
	PRIMARY KEY(type_id, file_id)
);

CREATE TABLE IF NOT EXISTS asset_archives (
	type_id INTEGER NOT NULL,
	file_id INTEGER NOT NULL,
	archive_id TEXT NOT NULL,
	PRIMARY KEY(type_id, file_id, archive_id),
	FOREIGN KEY(archive_id) REFERENCES archives(archive_id)
);

CREATE INDEX IF NOT EXISTS ix_asset_archives_archive ON asset_archives(archive_id);
";
			await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
		}

		await SetMetaAsync(connection, "schema_version", SchemaVersion.ToString(), cancellationToken).ConfigureAwait(false);
	}

	public static async Task SetMetaAsync(SqliteConnection connection, string key, string value, CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = @"INSERT INTO meta(key,value) VALUES($key,$value)
ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
		cmd.Parameters.AddWithValue("$key", key);
		cmd.Parameters.AddWithValue("$value", value);
		await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	public static async Task<string?> GetMetaAsync(SqliteConnection connection, string key, CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = "SELECT value FROM meta WHERE key=$key";
		cmd.Parameters.AddWithValue("$key", key);
		var obj = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
		return obj?.ToString();
	}

	public static async Task ClearIndexDataAsync(SqliteConnection connection, CancellationToken cancellationToken)
	{
		await using var cmd = connection.CreateCommand();
		cmd.CommandText = @"
DELETE FROM asset_archives;
DELETE FROM assets;
DELETE FROM archives;
";
		await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}
}
