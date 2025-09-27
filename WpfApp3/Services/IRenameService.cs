using System.Collections.ObjectModel;

namespace LiberTeaManager.Services
{
    public interface IRenameService
    {
        bool TryRename(object target, string newName, ObservableCollection<MainModItem> mods);
    }
}
