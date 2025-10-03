using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：模组库持久化存储接口（读写对象树、Profile 等）。
// Purpose: Mod library persistence store interface (read/write object trees, profiles, etc.).
public interface IModLibraryStore
{
	ValueTask<LibrarySnapshot?> TryLoadAsync(CancellationToken cancellationToken = default);
	ValueTask SaveAsync(LibrarySnapshot snapshot, CancellationToken cancellationToken = default);
}
