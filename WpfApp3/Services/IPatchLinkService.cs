using System.Threading.Tasks;

namespace LiberTeaManager.Services
{
    public interface IPatchLinkService
    {
        Task ReorderAndLinkAsync(bool fullRebuild, bool logPerGroup);
        int HardLinkCount { get; }
        int SymLinkCount { get; }
        int CopyCount { get; }
    }
}
