using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiberTeaManager.Services
{
    public interface IImportService
    {
        Task ImportArchivesAsync(IEnumerable<string> archivePaths);
        int ImportDirectory(string rootDirectory);
    }
}
