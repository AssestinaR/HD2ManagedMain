using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：提供不依赖 SQLite、Adaptation 或 Patch 内部解析的基础 FileFacts。
// Purpose: Provides filesystem-only FileFacts without SQLite, Adaptation or patch-body parsing.
public sealed class ModFileFactsProducer : IModFileFactsProducer
{
	private readonly IPatchFileIndexBuilder _indexBuilder;

	public ModFileFactsProducer(IPatchFileIndexBuilder indexBuilder)
	{
		_indexBuilder = indexBuilder ?? throw new ArgumentNullException(nameof(indexBuilder));
	}

	public ValueTask<PatchFileIndex> ProduceAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default)
		=> _indexBuilder.BuildAsync(snapshot, modsRootDirectory, cancellationToken);
}