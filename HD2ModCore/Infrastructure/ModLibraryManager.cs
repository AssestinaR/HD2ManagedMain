using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：基于 IModLibraryStore 的库管理器实现，提供删除节点/维护 Profile/更新元数据等操作。
// Purpose: IModLibraryStore-backed library manager providing delete node, profile maintenance and metadata updates.
public sealed class ModLibraryManager : IModLibraryManager
{
	private readonly StoragePaths _paths;
	private readonly IModLibraryStore _store;

	public ModLibraryManager(StoragePaths paths, IModLibraryStore store)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_store = store ?? throw new ArgumentNullException(nameof(store));
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
			Profiles: new List<Profile>());

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
			.Select(p => p with { Entries = p.Entries.Where(e => e.NodeId != nodeId).ToList() })
			.ToList();

		if (deleteStoredFiles)
		{
			TryDeleteStoredRoot(node.RelativePath);
		}

		var updated = snapshot with { Nodes = nodes, Profiles = profiles, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
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
		var updated = snapshot with { Profiles = profiles, SavedUtc = DateTimeOffset.UtcNow };
		await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		return updated;
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

	private void TryDeleteStoredRoot(string nodeRelativePath)
	{
		try
		{
			var parts = nodeRelativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
			{
				return;
			}

			// Stored path is mods/<importId>/..., so the 1st segment is the import root.
			var importRoot = Path.Combine(_paths.ModsDirectory, parts[0]);
			if (Directory.Exists(importRoot))
			{
				Directory.Delete(importRoot, recursive: true);
			}
		}
		catch
		{
			// ignore
		}
	}
}
