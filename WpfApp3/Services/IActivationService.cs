using System.Threading.Tasks;

namespace LiberTeaManager.Services
{
    public interface IActivationService
    {
        Task<(int hard, int sym, int copy)> EnableSelectedAsync(bool logPerGroup);
        Task DisableSelectedAsync();
        Task DeleteSelectedAsync();
    }
}
