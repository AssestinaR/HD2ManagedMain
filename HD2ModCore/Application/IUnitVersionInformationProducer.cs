using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：独立生产单个 Mod 的 Unit 版本信息，不把版本检测绑定到普通 AssetInventory。
// Purpose: Produces Unit-version information independently from ordinary AssetInventory.
public interface IUnitVersionInformationProducer
{
	ValueTask<ModUnitVersionFacts> ProduceAsync(
		ModNode node,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}
