using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：扫描节点目录内所有 .patch_n 文件的 TOC 来聚合资产键集合（缓存到内存以避免重复扫描）。
// Purpose: Aggregates AssetKeys by scanning TOCs of .patch_n files within a node directory (in-memory cached to avoid re-scans).
public sealed class AssetKeySetProvider : IAssetKeySetProvider
{
	private readonly IPatchFileNameParser _parser;
	private readonly IPatchTocScanner _scanner;
	private readonly Dictionary<string, HashSet<AssetKey>> _cache = new(StringComparer.OrdinalIgnoreCase);

	public AssetKeySetProvider(IPatchFileNameParser parser, IPatchTocScanner scanner)
	{
		_parser = parser ?? throw new ArgumentNullException(nameof(parser));
		_scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
	}

	public async ValueTask<IReadOnlySet<AssetKey>> GetAssetKeysAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		if (node is null)
		{
			throw new ArgumentNullException(nameof(node));
		}
		if (string.IsNullOrWhiteSpace(modsRootDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(modsRootDirectory));
		}

		var nodeDir = Path.Combine(modsRootDirectory, node.RelativePath);
		var key = Path.GetFullPath(nodeDir);

		lock (_cache)
		{
			if (_cache.TryGetValue(key, out var cached))
			{
				return cached;
			}
		}

		var result = new HashSet<AssetKey>();
		if (!Directory.Exists(nodeDir))
		{
			Cache(key, result);
			return result;
		}

		foreach (var filePath in Directory.EnumerateFiles(nodeDir, "*", SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var name = Path.GetFileName(filePath);
			if (!_parser.TryParse(name, out var info) || info is null)
			{
				continue;
			}
			if (info.SidecarKind != PatchSidecarKind.Base)
			{
				continue;
			}

			var keys = await _scanner.ScanAssetKeysAsync(filePath, cancellationToken).ConfigureAwait(false);
			result.UnionWith(keys);
		}

		Cache(key, result);
		return result;
	}

	private void Cache(string key, HashSet<AssetKey> value)
	{
		lock (_cache)
		{
			_cache[key] = value;
		}
	}
}
