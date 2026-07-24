using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：定义单一信息产品的生产器边界，信息中心只负责编排。
// Purpose: Defines a single information product producer; orchestration stays in the information center.
public interface IModInformationProducer<T>
{
	ModInformationKind Kind { get; }
	ValueTask<T> ProduceAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		IReadOnlySet<ModNodeId>? nodeIds,
		CancellationToken cancellationToken = default);
}