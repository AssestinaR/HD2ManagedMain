using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure.Json;

namespace HD2ModCore.Infrastructure;

// 作用：使用 JSON 文件持久化模组库（便于调试与便携，后续可迁移为 SQLite）。
// Purpose: Persists the mod library as a JSON file (portable and debuggable; can later migrate to SQLite).
public sealed class JsonModLibraryStore : IModLibraryStore
{
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Converters =
		{
            new ModNodeIdJsonConverter(),
			new ProfileIdJsonConverter(),
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	private const int CurrentVersion = 1;
	private readonly StoragePaths _paths;

	public JsonModLibraryStore(StoragePaths paths)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
	}

	private string SnapshotPath => Path.Combine(_paths.LibraryDirectory, "library.json");

	public async ValueTask<LibrarySnapshot?> TryLoadAsync(CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_paths.LibraryDirectory);
		if (!File.Exists(SnapshotPath))
		{
			return null;
		}

		try
		{
			var json = await File.ReadAllTextAsync(SnapshotPath, cancellationToken).ConfigureAwait(false);
			var snapshot = JsonSerializer.Deserialize<LibrarySnapshot>(json, SerializerOptions);
			return snapshot;
		}
		catch
		{
			return null;
		}
	}

	public async ValueTask SaveAsync(LibrarySnapshot snapshot, CancellationToken cancellationToken = default)
	{
		if (snapshot is null)
		{
			throw new ArgumentNullException(nameof(snapshot));
		}

		Directory.CreateDirectory(_paths.LibraryDirectory);

		var normalized = snapshot with
		{
			Version = snapshot.Version <= 0 ? CurrentVersion : snapshot.Version,
			SavedUtc = snapshot.SavedUtc == default ? DateTimeOffset.UtcNow : snapshot.SavedUtc,
		};

		var json = JsonSerializer.Serialize(normalized, SerializerOptions);
		var tmp = SnapshotPath + ".tmp";
		await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
		File.Copy(tmp, SnapshotPath, overwrite: true);
		File.Delete(tmp);
	}
}
