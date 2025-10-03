using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：基于 IAssetKeySetProvider 的冲突检测器，找出节点两两之间的 AssetKey 交集（可用于 UI 展示冲突）。
// Purpose: Conflict detector using IAssetKeySetProvider to find pairwise AssetKey intersections (for UI conflict display).
public sealed class ConflictDetector : IConflictDetector
{
	private readonly IAssetKeySetProvider _keySetProvider;

	public ConflictDetector(IAssetKeySetProvider keySetProvider)
	{
		_keySetProvider = keySetProvider ?? throw new ArgumentNullException(nameof(keySetProvider));
	}

	public async ValueTask<IReadOnlyList<ConflictPair>> DetectNodeConflictsAsync(
		IReadOnlyList<ModNodeId> nodeIds,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default)
	{
		if (nodeIds is null)
		{
			throw new ArgumentNullException(nameof(nodeIds));
		}
		if (snapshot is null)
		{
			throw new ArgumentNullException(nameof(snapshot));
		}

		var resolved = new List<(ModNodeId Id, HashSet<AssetKey> Keys)>();
		foreach (var id in nodeIds.Distinct())
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!snapshot.Nodes.TryGetValue(id, out var node))
			{
				continue;
			}
			var keys = await _keySetProvider.GetAssetKeysAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
			resolved.Add((id, new HashSet<AssetKey>(keys)));
		}

		var results = new List<ConflictPair>();
		for (var i = 0; i < resolved.Count; i++)
		{
			for (var j = i + 1; j < resolved.Count; j++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var a = resolved[i];
				var b = resolved[j];

				// Intersect smaller into larger for speed.
				var small = a.Keys.Count <= b.Keys.Count ? a.Keys : b.Keys;
				var large = ReferenceEquals(small, a.Keys) ? b.Keys : a.Keys;

				var shared = new List<AssetKey>();
				foreach (var k in small)
				{
					if (large.Contains(k))
					{
						shared.Add(k);
					}
				}

				if (shared.Count > 0)
				{
					results.Add(new ConflictPair(a.Id, b.Id, shared));
				}
			}
		}

		return results;
	}
}
