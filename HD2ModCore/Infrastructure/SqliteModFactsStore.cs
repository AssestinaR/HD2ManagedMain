using System.Text.Json;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using Microsoft.Data.Sqlite;

namespace HD2ModCore.Infrastructure;

// Purpose: Stores stable per-Mod patch assets, references and evidence in an independently versioned SQLite database.
public sealed class SqliteModFactsStore : IModFactsStore
{
	private const int SchemaVersion = 3;
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly StoragePaths paths;

	public SqliteModFactsStore(StoragePaths paths) => this.paths = paths ?? throw new ArgumentNullException(nameof(paths));

	public async ValueTask<PatchGroupAnalysisCacheEntry?> TryLoadAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT cache_json FROM mod_fact_snapshots WHERE node_id = $node";
		command.Parameters.AddWithValue("$node", nodeId.Value.ToString("N"));
		var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
		return json is null ? null : JsonSerializer.Deserialize<PatchGroupAnalysisCacheEntry>(json, JsonOptions);
	}

	public async ValueTask SaveAsync(PatchGroupAnalysisCacheEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		var node = entry.NodeId.Value.ToString("N");
		await ExecuteAsync(connection, transaction, "DELETE FROM asset_references WHERE node_id = $node; DELETE FROM mod_assets WHERE node_id = $node; DELETE FROM patch_groups WHERE node_id = $node;", node, cancellationToken).ConfigureAwait(false);
		foreach (var analysis in entry.Analyses)
		{
			var groupId = analysis.Input.PatchTocFilePath;
			await using (var groupCommand = connection.CreateCommand())
			{
				groupCommand.Transaction = (SqliteTransaction)transaction;
				groupCommand.CommandText = "INSERT INTO patch_groups(group_id,node_id,patch_path,analyzer_version) VALUES($group,$node,$path,$version)";
				groupCommand.Parameters.AddWithValue("$group", groupId); groupCommand.Parameters.AddWithValue("$node", node); groupCommand.Parameters.AddWithValue("$path", analysis.Input.PatchTocFilePath); groupCommand.Parameters.AddWithValue("$version", analysis.AnalyzerVersion);
				await groupCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}
			foreach (var asset in analysis.Assets)
			{
				await using var assetCommand = connection.CreateCommand(); assetCommand.Transaction = (SqliteTransaction)transaction;
				assetCommand.CommandText = "INSERT INTO mod_assets(node_id,group_id,type_id,file_id,toc_size,stream_size,gpu_size) VALUES($node,$group,$type,$file,$toc,$stream,$gpu)";
				assetCommand.Parameters.AddWithValue("$node", node); assetCommand.Parameters.AddWithValue("$group", groupId); assetCommand.Parameters.AddWithValue("$type", Hex(asset.AssetKey.TypeId)); assetCommand.Parameters.AddWithValue("$file", Hex(asset.AssetKey.FileId)); assetCommand.Parameters.AddWithValue("$toc", asset.TocDataSize); assetCommand.Parameters.AddWithValue("$stream", asset.StreamSize); assetCommand.Parameters.AddWithValue("$gpu", asset.GpuResourceSize);
				await assetCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}
			foreach (var reference in analysis.References)
			{
				await using var referenceCommand = connection.CreateCommand(); referenceCommand.Transaction = (SqliteTransaction)transaction;
				referenceCommand.CommandText = "INSERT INTO asset_references(node_id,group_id,source_type_id,source_file_id,target_type_id,target_file_id,relation_kind,payload_offset,slot_id,reference_index) VALUES($node,$group,$st,$sf,$tt,$tf,$kind,$offset,$slot,$idx)";
				referenceCommand.Parameters.AddWithValue("$node", node); referenceCommand.Parameters.AddWithValue("$group", groupId); referenceCommand.Parameters.AddWithValue("$st", Hex(reference.SourceAssetKey.TypeId)); referenceCommand.Parameters.AddWithValue("$sf", Hex(reference.SourceAssetKey.FileId)); referenceCommand.Parameters.AddWithValue("$tt", Hex(reference.TargetAssetKey.TypeId)); referenceCommand.Parameters.AddWithValue("$tf", Hex(reference.TargetAssetKey.FileId)); referenceCommand.Parameters.AddWithValue("$kind", (int)reference.Kind); referenceCommand.Parameters.AddWithValue("$offset", reference.PayloadRelativeOffset); referenceCommand.Parameters.AddWithValue("$slot", (object?)reference.SlotId ?? DBNull.Value); referenceCommand.Parameters.AddWithValue("$idx", (object?)reference.ReferenceIndex ?? DBNull.Value);
				await referenceCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		await using (var snapshotCommand = connection.CreateCommand())
		{
			snapshotCommand.Transaction = (SqliteTransaction)transaction;
			snapshotCommand.CommandText = "INSERT INTO mod_fact_snapshots(node_id,relative_path,cache_json,built_utc) VALUES($node,$path,$json,$built) ON CONFLICT(node_id) DO UPDATE SET relative_path=excluded.relative_path,cache_json=excluded.cache_json,built_utc=excluded.built_utc";
			snapshotCommand.Parameters.AddWithValue("$node", node); snapshotCommand.Parameters.AddWithValue("$path", entry.RelativePath); snapshotCommand.Parameters.AddWithValue("$json", JsonSerializer.Serialize(entry, JsonOptions)); snapshotCommand.Parameters.AddWithValue("$built", entry.BuiltAtUtc.UtcDateTime.ToString("O"));
			await snapshotCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
		}
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask DeleteAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		await ExecuteAsync(connection, transaction, "DELETE FROM asset_references WHERE node_id = $node; DELETE FROM mod_assets WHERE node_id = $node; DELETE FROM patch_groups WHERE node_id = $node; DELETE FROM mod_fact_snapshots WHERE node_id = $node;", nodeId.Value.ToString("N"), cancellationToken).ConfigureAwait(false);
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<IReadOnlyList<PatchAssetReference>> FindConsumersAsync(HD2ModAdaptation.PatchReconstruction.AssetKey targetAssetKey, CancellationToken cancellationToken = default)
	{
		return (await FindConsumerFactsAsync(targetAssetKey, cancellationToken).ConfigureAwait(false))
			.Select(consumer => consumer.Reference)
			.ToArray();
	}

	public async ValueTask<IReadOnlyList<ModAssetConsumerFact>> FindConsumerFactsAsync(HD2ModAdaptation.PatchReconstruction.AssetKey targetAssetKey, CancellationToken cancellationToken = default)
	{
		await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT node_id,group_id,source_type_id,source_file_id,relation_kind,payload_offset,slot_id,reference_index FROM asset_references WHERE target_type_id=$type AND target_file_id=$file";
		command.Parameters.AddWithValue("$type", Hex(targetAssetKey.TypeId));
		command.Parameters.AddWithValue("$file", Hex(targetAssetKey.FileId));
		var result = new List<ModAssetConsumerFact>();
		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var source = new HD2ModAdaptation.PatchReconstruction.AssetKey(ParseHex(reader.GetString(2)), ParseHex(reader.GetString(3)));
			var reference = new PatchAssetReference(source, targetAssetKey, (PatchReferenceKind)reader.GetInt32(4), checked((uint)reader.GetInt64(5)), reader.IsDBNull(6) ? null : checked((uint)reader.GetInt64(6)), reader.IsDBNull(7) ? null : reader.GetInt32(7));
			result.Add(new ModAssetConsumerFact(new ModNodeId(Guid.ParseExact(reader.GetString(0), "N")), reader.GetString(1), reference));
		}
		return result;
	}

	private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(paths.IndexDirectory); var connection = new SqliteConnection($"Data Source={paths.ModFactsDbPath}"); await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using (var cleanup = connection.CreateCommand()) { cleanup.CommandText = "DROP TABLE IF EXISTS unit_mesh_parts;"; await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
		await using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; CREATE TABLE IF NOT EXISTS schema_info(version INTEGER NOT NULL); INSERT INTO schema_info(version) SELECT {SchemaVersion} WHERE NOT EXISTS(SELECT 1 FROM schema_info); CREATE TABLE IF NOT EXISTS mod_fact_snapshots(node_id TEXT PRIMARY KEY,relative_path TEXT NOT NULL,cache_json TEXT NOT NULL,built_utc TEXT NOT NULL); CREATE TABLE IF NOT EXISTS patch_groups(group_id TEXT PRIMARY KEY,node_id TEXT NOT NULL,patch_path TEXT NOT NULL,analyzer_version TEXT NOT NULL); CREATE TABLE IF NOT EXISTS mod_assets(node_id TEXT NOT NULL,group_id TEXT NOT NULL,type_id TEXT NOT NULL,file_id TEXT NOT NULL,toc_size INTEGER NOT NULL,stream_size INTEGER NOT NULL,gpu_size INTEGER NOT NULL,PRIMARY KEY(node_id,group_id,type_id,file_id)); CREATE INDEX IF NOT EXISTS ix_mod_assets_key ON mod_assets(type_id,file_id); CREATE TABLE IF NOT EXISTS asset_references(node_id TEXT NOT NULL,group_id TEXT NOT NULL,source_type_id TEXT NOT NULL,source_file_id TEXT NOT NULL,target_type_id TEXT NOT NULL,target_file_id TEXT NOT NULL,relation_kind INTEGER NOT NULL,payload_offset INTEGER NOT NULL,slot_id INTEGER NULL,reference_index INTEGER NULL); CREATE INDEX IF NOT EXISTS ix_asset_refs_source ON asset_references(source_type_id,source_file_id); CREATE INDEX IF NOT EXISTS ix_asset_refs_target ON asset_references(target_type_id,target_file_id);"; await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); return connection;
	}

	private static async ValueTask ExecuteAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string sql, string node, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.Transaction = (SqliteTransaction)transaction; command.CommandText = sql; command.Parameters.AddWithValue("$node", node); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
	private static string Hex(ulong value) => value.ToString("x16");
	private static ulong ParseHex(string value) => Convert.ToUInt64(value, 16);
}