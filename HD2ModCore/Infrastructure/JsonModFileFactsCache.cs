using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：以原子替换 JSON 文件持久化 FileFacts，不依赖 SQLite。
// Purpose: Persists FileFacts as atomically replaced JSON without SQLite.
public sealed class JsonModFileFactsCache : IModFileFactsCache
{
	private const string ProducerVersion = "filesystem-v1";
	private const int SchemaVersion = 1;
	private readonly string _directory;
	private static readonly JsonSerializerOptions Options = CreateOptions();

	private static JsonSerializerOptions CreateOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };
		options.Converters.Add(new ModNodeIdJsonConverter());
		return options;
	}

	public JsonModFileFactsCache(StoragePaths paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		_directory = Path.Combine(paths.IndexDirectory, "mod-information");
	}

	public async ValueTask<PatchFileIndex?> TryLoadAsync(string generation, CancellationToken cancellationToken = default)
	{
		var path = GetPath(generation);
		if (!File.Exists(path)) return null;
		await using var stream = File.OpenRead(path);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
		if (document.RootElement.TryGetProperty("facts", out _))
			return document.RootElement.Deserialize<CacheEnvelope>(Options)?.Facts;
		return document.RootElement.Deserialize<PatchFileIndex>(Options);
	}

	public async ValueTask SaveAsync(string generation, PatchFileIndex facts, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_directory);
		var path = GetPath(generation);
		var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			await using (var stream = File.Create(temporary))
				await JsonSerializer.SerializeAsync(stream, new CacheEnvelope(facts, generation, ProducerVersion, SchemaVersion, DateTimeOffset.UtcNow), Options, cancellationToken).ConfigureAwait(false);
			File.Move(temporary, path, true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	public async ValueTask DeleteNodeAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		if (!Directory.Exists(_directory)) return;

		foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				var containsNode = false;
				await using (var stream = File.OpenRead(path))
				using (var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false))
				{
					var root = document.RootElement;
					var facts = root.ValueKind == JsonValueKind.Object
						&& TryGetPropertyIgnoreCase(root, "facts", out var envelopeFacts)
						? envelopeFacts
						: root;
					containsNode = ContainsNode(facts, nodeId);
				}
				if (containsNode && File.Exists(path)) File.Delete(path);
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { return; }
			catch (IOException) { }
			catch (JsonException) { }
		}
	}

	private static bool ContainsNode(JsonElement facts, ModNodeId nodeId)
	{
		if (facts.ValueKind != JsonValueKind.Object
			|| !TryGetPropertyIgnoreCase(facts, "filesByNode", out var filesByNode)
			|| filesByNode.ValueKind != JsonValueKind.Object)
			return false;

		var id = nodeId.Value.ToString("N");
		return filesByNode.EnumerateObject().Any(property => string.Equals(property.Name, id, StringComparison.OrdinalIgnoreCase));
	}

	private string GetPath(string generation) => Path.Combine(_directory, Sanitize(generation) + ".json");
	private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}
		value = default;
		return false;
	}
	private static string Sanitize(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
	private sealed record CacheEnvelope(PatchFileIndex Facts, string Generation, string ProducerVersion, int SchemaVersion, DateTimeOffset BuiltUtc);

	private sealed class ModNodeIdJsonConverter : JsonConverter<ModNodeId>
	{
		public override ModNodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			=> new(Guid.Parse(reader.GetString()!));
		public override void Write(Utf8JsonWriter writer, ModNodeId value, JsonSerializerOptions options)
			=> writer.WriteStringValue(value.Value.ToString("N"));
		public override ModNodeId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			=> new(Guid.ParseExact(reader.GetString()!, "N"));
		public override void WriteAsPropertyName(Utf8JsonWriter writer, ModNodeId value, JsonSerializerOptions options)
			=> writer.WritePropertyName(value.Value.ToString("N"));
	}
}