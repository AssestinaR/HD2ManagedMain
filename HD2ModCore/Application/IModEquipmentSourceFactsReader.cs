using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：组合统一 Mod 信息、Patch/Unit 读取和 GameData 部件映射，提供装备来源事实。
// Purpose: Composes Mod facts, unified Patch/Unit reads, and GameData part mapping for equipment sources.
public interface IModEquipmentSourceFactsReader
{
	ValueTask<ModEquipmentSourceFacts> ReadAsync(
		ModNode source,
		string modsRootDirectory,
		ModInformationRequest request,
		CancellationToken cancellationToken = default);

	void ClearOperation(Guid operationId);
}
