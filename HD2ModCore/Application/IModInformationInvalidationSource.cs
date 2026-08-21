using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：为可选的派生属性生产器广播 Mod 内容失效，不改变 IModInformationReader 的既有公共契约。
// Purpose: Optionally broadcasts Mod-content invalidation to derived producers without changing IModInformationReader.
public interface IModInformationInvalidationSource
{
	event Action<ModNodeId>? NodeInvalidated;
}
