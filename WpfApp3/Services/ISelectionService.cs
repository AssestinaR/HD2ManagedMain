using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace LiberTeaManager.Services
{
    public interface ISelectionService
    {
        bool HandleMouseDown(object dataContext, bool ctrl, bool shift, ObservableCollection<MainModItem> roots, ref List<object> lastShiftRange, out bool dragCandidate);
        IEnumerable<object> GetAllSelected(ObservableCollection<MainModItem> roots);
        bool ReorderAfterDrop(object targetItem, List<object> dragged, ObservableCollection<MainModItem> roots);
    }
}
