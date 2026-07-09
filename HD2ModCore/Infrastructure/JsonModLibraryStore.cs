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
	private sealed record LibraryFileSnapshot(
		int Version,
		DateTimeOffset SavedUtc,
		IReadOnlyDictionary<ModNodeId, ModNode> Nodes);

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

	private const int CurrentVersion = 1;
	private readonly StoragePaths _paths;
	private readonly JsonProfileStore _profileStore;

	public JsonModLibraryStore(StoragePaths paths)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_profileStore = new JsonProfileStore(paths);
	}

	private string SnapshotPath => _paths.LibraryPath;

	public async ValueTask<LibrarySnapshot?> TryLoadAsync(CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_paths.ModsDirectory);
		if (!File.Exists(SnapshotPath))
		{
			var legacySnapshot = await TryLoadLegacyAsync(cancellationToken).ConfigureAwait(false);
			if (legacySnapshot is null)
			{
				return null;
			}

			await SaveAsync(legacySnapshot, cancellationToken).ConfigureAwait(false);
			return legacySnapshot;
		}

		try
		{
			var json = await File.ReadAllTextAsync(SnapshotPath, cancellationToken).ConfigureAwait(false);
			var fileSnapshot = JsonSerializer.Deserialize<LibraryFileSnapshot>(json, SerializerOptions);
			if (fileSnapshot is null)
			{
				return null;
			}

			var profiles = await LoadProfilesAsync(Array.Empty<Profile>(), cancellationToken).ConfigureAwait(false);
			return new LibrarySnapshot(fileSnapshot.Version, fileSnapshot.SavedUtc, fileSnapshot.Nodes, profiles);
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

		Directory.CreateDirectory(_paths.ModsDirectory);

		var normalized = snapshot with
		{
			Version = snapshot.Version <= 0 ? CurrentVersion : snapshot.Version,
			SavedUtc = snapshot.SavedUtc == default ? DateTimeOffset.UtcNow : snapshot.SavedUtc,
			Profiles = Array.Empty<Profile>(),
		};

		await _profileStore.SaveAsync(snapshot.Profiles, cancellationToken).ConfigureAwait(false);

		var fileSnapshot = new LibraryFileSnapshot(normalized.Version, normalized.SavedUtc, normalized.Nodes);
		var json = JsonSerializer.Serialize(fileSnapshot, SerializerOptions);
		var tmp = SnapshotPath + ".tmp";
		await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
		File.Copy(tmp, SnapshotPath, overwrite: true);
		File.Delete(tmp);
	}

	private async ValueTask<IReadOnlyList<Profile>> LoadProfilesAsync(IReadOnlyList<Profile> legacyProfiles, CancellationToken cancellationToken)
	{
		var profiles = await _profileStore.TryLoadAsync(cancellationToken).ConfigureAwait(false);
		if (profiles.Count > 0)
		{
			return profiles;
		}

		if (legacyProfiles.Count > 0)
		{
			await _profileStore.SaveAsync(legacyProfiles, cancellationToken).ConfigureAwait(false);
			return legacyProfiles;
		}

		return Array.Empty<Profile>();
	}

	private async ValueTask<LibrarySnapshot?> TryLoadLegacyAsync(CancellationToken cancellationToken)
	{
		var legacyPath = Path.Combine(_paths.DataDirectory, "library", "library.json");
		if (!File.Exists(legacyPath))
		{
			return null;
		}

		try
		{
			var json = await File.ReadAllTextAsync(legacyPath, cancellationToken).ConfigureAwait(false);
			var snapshot = JsonSerializer.Deserialize<LibrarySnapshot>(json, SerializerOptions);
			if (snapshot is null)
			{
				return null;
			}

			var profiles = await LoadProfilesAsync(snapshot.Profiles, cancellationToken).ConfigureAwait(false);
			return snapshot with { Profiles = profiles };
		}
		catch
		{
			return null;
		}
	}
}
