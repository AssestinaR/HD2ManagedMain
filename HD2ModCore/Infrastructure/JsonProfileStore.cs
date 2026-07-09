using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure.Json;

namespace HD2ModCore.Infrastructure;

// 作用：使用独立 JSON 文件持久化 Profile，避免配置状态混入可迁移的 mods/library.json。
// Purpose: Persists Profiles in a separate JSON file so user profile state does not mix into portable mods/library.json.
public sealed class JsonProfileStore
{
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		Converters =
		{
			new ModNodeIdJsonConverter(),
			new ProfileIdJsonConverter(),
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	private readonly StoragePaths _paths;

	public JsonProfileStore(StoragePaths paths)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
	}

	public async ValueTask<IReadOnlyList<Profile>> TryLoadAsync(CancellationToken cancellationToken = default)
	{
		if (!File.Exists(_paths.ProfilesPath))
		{
			return Array.Empty<Profile>();
		}

		try
		{
			var json = await File.ReadAllTextAsync(_paths.ProfilesPath, cancellationToken).ConfigureAwait(false);
			var profiles = JsonSerializer.Deserialize<List<Profile>>(json, SerializerOptions);
			return profiles ?? (IReadOnlyList<Profile>)Array.Empty<Profile>();
		}
		catch
		{
			return Array.Empty<Profile>();
		}
	}

	public async ValueTask SaveAsync(IReadOnlyList<Profile> profiles, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_paths.DataDirectory);
		var json = JsonSerializer.Serialize(profiles ?? (IReadOnlyList<Profile>)Array.Empty<Profile>(), SerializerOptions);
		var tmp = _paths.ProfilesPath + ".tmp";
		await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
		File.Copy(tmp, _paths.ProfilesPath, overwrite: true);
		File.Delete(tmp);
	}
}
