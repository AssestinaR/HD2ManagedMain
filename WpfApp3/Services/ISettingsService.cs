using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiberTeaManager.Services
{
    public interface ISettingsService
    {
        string ModFolder { get; set; }
        string GameFolder { get; set; }
        double MainWindowWidth { get; set; }
        double MainWindowHeight { get; set; }
        bool FastImport { get; set; }
        // 新增: 多配置支持
        string CurrentProfile { get; set; }
        Dictionary<string, string> ProfileModFolders { get; }
        void Load();
        void Save();
    }
}
