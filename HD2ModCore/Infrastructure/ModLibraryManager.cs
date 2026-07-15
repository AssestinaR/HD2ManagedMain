using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：基于 IModLibraryStore 的库管理器实现，提供删除节点/维护 Profile/更新元数据等操作。
// Purpose: IModLibraryStore-backed library manager providing delete node, profile maintenance and metadata updates.
public sealed class ModLibraryManager : IModLibraryManager
{
	private readonly StoragePaths _paths;
	private readonly IModLibraryStore _store;
	private readonly IPatchFileGroupFingerprintStore? _fingerprintStore;
	private readonly IModFactsStore? _modFactsStore;

	public ModLibraryManager(StoragePaths paths, IModLibraryStore store, IPatchFileGroupFingerprintStore? fingerprintStore = null, IModFactsStore? modFactsStore = null)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_fingerprintStore = fingerprintStore;
		_modFactsStore = modFactsStore;
	}

	public async ValueTask<LibrarySnapshot> LoadOrCreateAsync(CancellationToken cancellationToken = default)
	{
		var loaded = await _store.TryLoadAsync(cancellationToken).ConfigureAwait(false);
		if (loaded is not null)
		{
			return loaded;
		}

		var empty = new LibrarySnapshot(
			Version: 1,
			SavedUtc: DateTimeOffset.UtcNow,
			Nodes: new Dictionary<ModNodeId, ModNode>(),
			Profiles: new List<Profile>(),
			ActiveProfileId: null);

		await _store.SaveAsync(empty, cancellationToken).ConfigureAwait(false);
		return empty;
	}

	public async ValueTask<LibrarySnapshot> DeleteNodeAsync(ModNodeId nodeId, bool deleteStoredFiles, CancellationToken cancellationToken = default)
	{
		var snapshot = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		var nodes = new Dictionary<ModNodeId, ModNode>(snapshot.Nodes);

		if (!nodes.TryGetValue(nodeId, out var node))
		{
			return snapshot;
		}

		// Remove the node itself.
		nodes.Remove(nodeId);

		// Remove references from other nodes' children.
		foreach (var kv in nodes.ToList())
		{
			var updatedChildren = kv.Value.Children.Where(c => c != nodeId).ToList();
			if (updatedChildren.Count != kv.Value.Children.Count)
			{
				nodes[kv.Key] = kv.Value with { Children = updatedChildren };
			}
		}

		// Remove from profiles.
		var profiles = snapshot.Profiles
			.Select(p =>
			{
				var entries = NormalizeEntryOrder(p.Entries.Where(e => e.NodeId != nodeId).ToList());
				return entries.SequenceEqual(p.Entries)
					? p
					: p with { Entries = entries, ModifiedUtc = DateTimeOffset.UtcNow, Revision = checked(p.Revision + 1) };
			})
			.ToList();

		if (deleteStoredFiles)
		{
			TryDeleteStoredRoot(node.RelativePath);
		}
		if (_modFactsStore is not null) await _modFactsStore.DeleteAsync(nodeId, cancellationToken).ConfigureAwait(false);

		var updated = snapshot with { Nodes = nodes, Profiles = profiles, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		if (_fingerprintStore is not null)
		{
			var manifest = await _fingerprintStore.TryLoadAsync(cancellationToken).ConfigureAwait(false);
			if (manifest is not null && manifest.Nodes.ContainsKey(nodeId))
			{
				var remaining = manifest.Nodes
					.Where(pair => pair.Key != nodeId)
					.ToDictionary(pair => pair.Key, pair => pair.Value);
				await _fingerprintStore.SaveAsync(manifest with { BuiltUtc = DateTimeOffset.UtcNow, Nodes = remaining }, cancellationToken).ConfigureAwait(false);
			}
		}
		return updated;
	}

	public async ValueTask<LibrarySnapshot> UpsertProfileAsync(Profile profile, CancellationToken cancellationToken = default)
	{
		if (profile is null)
		{
			throw new ArgumentNullException(nameof(profile));
		}

		var snapshot = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		var profiles = snapshot.Profiles.ToList();

		var index = profiles.FindIndex(p => p.Id == profile.Id);
		var normalized = profile with
		{
			ModifiedUtc = DateTimeOffset.UtcNow,
		};

		if (index >= 0)
		{
			profiles[index] = normalized;
		}
		else
		{
			profiles.Add(normalized with { CreatedUtc = DateTimeOffset.UtcNow });
		}

		var updated = snapshot with { Profiles = profiles, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		return updated;
	}

	public async ValueTask<LibrarySnapshot> DeleteProfileAsync(ProfileId profileId, CancellationToken cancellationToken = default)
	{
		var snapshot = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		var profiles = snapshot.Profiles.Where(p => p.Id != profileId).ToList();
		var activeProfileId = snapshot.ActiveProfileId == profileId ? null : snapshot.ActiveProfileId;
		var updated = snapshot with { Profiles = profiles, ActiveProfileId = activeProfileId, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		return updated;
	}

	public async ValueTask<LibrarySnapshot> RenameProfileAsync(ProfileId profileId, string newName, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(newName))
		{
			throw new ArgumentException("Profile name cannot be empty.", nameof(newName));
		}

		var snapshot = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		var profiles = snapshot.Profiles.ToList();
		var index = profiles.FindIndex(p => p.Id == profileId);
		if (index < 0)
		{
			return snapshot;
		}

		var normalizedName = newName.Trim();
		if (profiles.Any(p => p.Id != profileId && string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidOperationException($"Profile name already exists: {normalizedName}");
		}

		profiles[index] = profiles[index] with { Name = normalizedName, ModifiedUtc = DateTimeOffset.UtcNow };
		var updated = snapshot with { Profiles = profiles, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		return updated;
	}

	public async ValueTask<LibrarySnapshot> SetActiveProfileAsync(ProfileId? profileId, CancellationToken cancellationToken = default)
	{
		var snapshot = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		if (profileId is not null && snapshot.Profiles.All(profile => profile.Id != profileId.Value))
		{
			throw new InvalidOperationException($"Profile does not exist: {profileId.Value.Value:N}");
		}

		if (snapshot.ActiveProfileId == profileId)
		{
			return snapshot;
		}

		var updated = snapshot with { ActiveProfileId = profileId, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		return updated;
	}

	public async ValueTask<LibrarySnapshot> AddProfileEntryAsync(ProfileId profileId, ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		var snapshot = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		if (!snapshot.Nodes.ContainsKey(nodeId))
		{
			throw new InvalidOperationException($"Mod node does not exist: {nodeId.Value:N}");
		}

		return await UpdateProfileEntriesAsync(
			profileId,
			entries =>
			{
				if (entries.Any(e => e.NodeId == nodeId))
				{
					return entries;
				}

				var nextOrder = entries.Count == 0 ? 0 : entries.Max(e => e.LoadOrder) + 1;
				entries.Add(new ProfileEntry(nodeId, nextOrder));
				return NormalizeEntryOrder(entries);
			},
			cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<LibrarySnapshot> RemoveProfileEntryAsync(ProfileId profileId, ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		return await UpdateProfileEntriesAsync(
			profileId,
			entries => NormalizeEntryOrder(entries.Where(e => e.NodeId != nodeId).ToList()),
			cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<LibrarySnapshot> MoveProfileEntryAsync(ProfileId profileId, ModNodeId nodeId, int direction, CancellationToken cancellationToken = default)
	{
		if (direction == 0)
		{
			return await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		}

		return await UpdateProfileEntriesAsync(
			profileId,
			entries =>
			{
				var ordered = entries.OrderBy(e => e.LoadOrder).ThenBy(e => e.AddedUtc).ToList();
				var index = ordered.FindIndex(e => e.NodeId == nodeId);
				if (index < 0)
				{
					return ordered;
				}

				var target = direction < 0 ? index - 1 : index + 1;
				if (target < 0 || target >= ordered.Count)
				{
					return NormalizeEntryOrder(ordered);
				}

				(ordered[index], ordered[target]) = (ordered[target], ordered[index]);
				return NormalizeEntryOrder(ordered);
			},
			cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<LibrarySnapshot> UpdateNodeMetadataAsync(ModNodeId nodeId, ModNodeMetadata metadata, CancellationToken cancellationToken = default)
	{
		var snapshot = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		var nodes = new Dictionary<ModNodeId, ModNode>(snapshot.Nodes);
		if (!nodes.TryGetValue(nodeId, out var node))
		{
			return snapshot;
		}

		nodes[nodeId] = node with { Metadata = metadata, RelativePath = node.RelativePath };
		var updated = snapshot with { Nodes = nodes, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		return updated;
	}

	private async ValueTask<LibrarySnapshot> UpdateProfileEntriesAsync(ProfileId profileId, Func<List<ProfileEntry>, IReadOnlyList<ProfileEntry>> update, CancellationToken cancellationToken)
	{
		var snapshot = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
		var profiles = snapshot.Profiles.ToList();
		var index = profiles.FindIndex(p => p.Id == profileId);
		if (index < 0)
		{
			return snapshot;
		}

		var profile = profiles[index];
		var entries = profile.Entries.OrderBy(e => e.LoadOrder).ThenBy(e => e.AddedUtc).ToList();
		var updatedEntries = update(entries);
		if (updatedEntries.SequenceEqual(profile.Entries))
		{
			return snapshot;
		}

		profiles[index] = profile with
		{
			Entries = updatedEntries,
			ModifiedUtc = DateTimeOffset.UtcNow,
			Revision = checked(profile.Revision + 1),
		};

		var updated = snapshot with { Profiles = profiles, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		return updated;
	}

	private static IReadOnlyList<ProfileEntry> NormalizeEntryOrder(IReadOnlyList<ProfileEntry> entries)
	{
		return entries
			.Select((entry, index) => entry with { LoadOrder = index })
			.ToList();
	}

	private void TryDeleteStoredRoot(string nodeRelativePath)
	{
		try
		{
			var parts = nodeRelativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
			{
				return;
			}

			var storedRoot = Path.GetFullPath(Path.Combine(_paths.ModsDirectory, nodeRelativePath));
			var modsRoot = Path.GetFullPath(_paths.ModsDirectory);
			if (!storedRoot.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			if (Directory.Exists(storedRoot))
			{
				SetReadOnlyRecursive(storedRoot, readOnly: false);
				Directory.Delete(storedRoot, recursive: true);
			}
		}
		catch
		{
			// ignore
		}
	}

	private static void SetReadOnlyRecursive(string directory, bool readOnly)
	{
		foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
		{
			var attributes = File.GetAttributes(path);
			File.SetAttributes(path, readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
		}
	}
}
