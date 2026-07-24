using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：以统一 JSON 信封持久化深度信息产品，失败时不影响旧文件或基础部署。
// Purpose: Persists deep information products in a common JSON envelope without affecting deployment.
public sealed class JsonModInformationCache : IModInformationCache
{
	private const string ProducerVersion = "information-center-v1";
	private const int SchemaVersion = 1;
	private readonly string _directory;
	private static readonly JsonSerializerOptions Options = CreateOptions();

	private static JsonSerializerOptions CreateOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new AssetKeySetConverter());
		return options;
	}

	public JsonModInformationCache(StoragePaths paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		_directory = Path.Combine(paths.IndexDirectory, "mod-information");
	}

	public async ValueTask<T?> TryLoadAsync<T>(ModInformationKind kind, ModNodeId nodeId, string generation, CancellationToken cancellationToken = default)
	{
		var path = GetPath(kind, nodeId, generation);
		if (!File.Exists(path)) return default;
		try
		{
			await using var stream = File.OpenRead(path);
			var envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope<T>>(stream, Options, cancellationToken).ConfigureAwait(false);
			if (envelope is not { SchemaVersion: SchemaVersion } || envelope.Generation != generation)
			{
				TryDelete(path);
				return default;
			}
			return envelope.Data;
		}
		catch (JsonException) { TryDelete(path); return default; }
		catch (IOException) { return default; }
	}

	public async ValueTask<ModInformationCacheEntry<T>?> TryLoadLatestAsync<T>(ModInformationKind kind, ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		if (!Directory.Exists(_directory)) return default;
		ModInformationCacheEntry<T>? latest = null;
		foreach (var path in Directory.EnumerateFiles(_directory, $"{kind}_{nodeId.Value:N}_*.json"))
		{
			try
			{
				await using var stream = File.OpenRead(path);
				var envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope<T>>(stream, Options, cancellationToken).ConfigureAwait(false);
				if (envelope is not { SchemaVersion: SchemaVersion } || envelope.NodeId != nodeId)
				{
					if (envelope is not { SchemaVersion: SchemaVersion }) TryDelete(path);
					continue;
				}
				if (latest is null || envelope.BuiltUtc > latest.BuiltUtc)
					latest = new ModInformationCacheEntry<T>(envelope.Generation, envelope.Data, envelope.BuiltUtc);
			}
			catch (JsonException) { TryDelete(path); }
			catch (IOException) { }
		}
		return latest;
	}

	public async ValueTask SaveAsync<T>(ModInformationKind kind, ModNodeId nodeId, string generation, T data, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_directory);
		var path = GetPath(kind, nodeId, generation);
		var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			await using (var stream = File.Create(temporary))
				await JsonSerializer.SerializeAsync(stream, new CacheEnvelope<T>(nodeId, generation, ProducerVersion, SchemaVersion, DateTimeOffset.UtcNow, data), Options, cancellationToken).ConfigureAwait(false);
			File.Move(temporary, path, true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	public ValueTask DeleteNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		if (Directory.Exists(_directory))
			foreach (var path in Directory.EnumerateFiles(_directory, $"*_{nodeId.Value:N}_*.json"))
				try { File.Delete(path); } catch (IOException) { }
		return ValueTask.CompletedTask;
	}

	private string GetPath(ModInformationKind kind, ModNodeId nodeId, string generation)
		=> Path.Combine(_directory, $"{kind}_{nodeId.Value:N}_{Sanitize(generation)}.json");
	private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } }
	private static string Sanitize(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
	private sealed record CacheEnvelope<T>(ModNodeId NodeId, string Generation, string ProducerVersion, int SchemaVersion, DateTimeOffset BuiltUtc, T Data);

	private sealed class AssetKeySetConverter : JsonConverter<IReadOnlySet<AssetKey>>
	{
		public override IReadOnlySet<AssetKey> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			=> JsonSerializer.Deserialize<HashSet<AssetKey>>(ref reader, options) ?? [];

		public override void Write(Utf8JsonWriter writer, IReadOnlySet<AssetKey> value, JsonSerializerOptions options)
			=> JsonSerializer.Serialize(writer, value.ToArray(), options);
	}
}
