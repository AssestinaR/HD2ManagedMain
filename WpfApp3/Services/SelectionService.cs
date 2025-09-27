using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiberTeaManager.Services
{
    internal sealed class SelectionService : ISelectionService
    {
        private static IEnumerable<object> GetSiblings(object item, ObservableCollection<MainModItem> roots)
        {
            if (item is MainModItem) return roots.Cast<object>();
            foreach (var root in roots)
            {
                if (root.Options.Contains(item as OptionItem)) return root.Options.Cast<object>();
                foreach (var opt in root.Options)
                    if (opt.SubOptions.Contains(item as SubOptionItem)) return opt.SubOptions.Cast<object>();
            }
            return Enumerable.Empty<object>();
        }
        private static void ClearAll(ObservableCollection<MainModItem> roots)
        { foreach (var m in roots) { m.IsSelected = false; foreach (var o in m.Options) { o.IsSelected = false; foreach (var s in o.SubOptions) s.IsSelected = false; } } }
        private static void SetSelected(object item, bool sel) { switch (item) { case MainModItem m: m.IsSelected = sel; break; case OptionItem o: o.IsSelected = sel; break; case SubOptionItem s: s.IsSelected = sel; break; } }
        private static bool IsSelected(object item) => item switch { MainModItem m => m.IsSelected, OptionItem o => o.IsSelected, SubOptionItem s => s.IsSelected, _ => false };

        public bool HandleMouseDown(object dataContext, bool ctrl, bool shift, ObservableCollection<MainModItem> roots, ref List<object> lastShiftRange, out bool dragCandidate)
        {
            dragCandidate = false;
            if (dataContext == null) return false;
            var wasSelected = IsSelected(dataContext);
            if (!ctrl && !shift && !wasSelected)
            { ClearAll(roots); SetSelected(dataContext, true); lastShiftRange = new List<object> { dataContext }; }
            else if (ctrl)
            { SetSelected(dataContext, !wasSelected); lastShiftRange = new List<object> { dataContext }; }
            else if (shift)
            {
                var siblings = GetSiblings(dataContext, roots).ToList(); object anchor = lastShiftRange.FirstOrDefault() ?? dataContext;
                int i1 = siblings.IndexOf(anchor); int i2 = siblings.IndexOf(dataContext);
                if (i1 >= 0 && i2 >= 0)
                {
                    if (!IsSelected(anchor)) SetSelected(anchor, true);
                    int from = Math.Min(i1, i2); int to = Math.Max(i1, i2);
                    foreach (var s in siblings.Skip(from).Take(to - from + 1)) SetSelected(s, true);
                    lastShiftRange = siblings.Skip(from).Take(to - from + 1).ToList();
                }
            }
            dragCandidate = wasSelected;
            return true;
        }

        public IEnumerable<object> GetAllSelected(ObservableCollection<MainModItem> roots)
        {
            foreach (var m in roots)
            {
                if (m.IsSelected) yield return m;
                foreach (var o in m.Options)
                { if (o.IsSelected) yield return o; foreach (var s in o.SubOptions) if (s.IsSelected) yield return s; }
            }
        }

        private static bool IsSameLevel(object a, object b, ObservableCollection<MainModItem> roots)
        {
            if (a == null || b == null) return false;
            if (a is MainModItem && b is MainModItem) return true;
            if (a is OptionItem ao && b is OptionItem bo) return roots.Any(m => m.Options.Contains(ao) && m.Options.Contains(bo));
            if (a is SubOptionItem asub && b is SubOptionItem bsub) return roots.SelectMany(m => m.Options).Any(o => o.SubOptions.Contains(asub) && o.SubOptions.Contains(bsub));
            return false;
        }

        public bool ReorderAfterDrop(object targetItem, List<object> dragged, ObservableCollection<MainModItem> roots)
        {
            if (targetItem == null || dragged == null || dragged.Count == 0) return false;
            var sameLevel = dragged.Where(d => IsSameLevel(d, targetItem, roots)).ToList();
            if (!sameLevel.Any()) return false;
            bool needPatch = false;
            if (targetItem is MainModItem)
            {
                ReorderCollection(roots.Cast<object>().ToList(), sameLevel, targetItem, list => ApplyOrder(roots, list.Cast<MainModItem>().ToList()));
                if (roots.Any(m => m.Enabled == EnabledState.Enabled && (sameLevel.Contains(m) || ReferenceEquals(m, targetItem)))) needPatch = true;
            }
            else if (targetItem is OptionItem opt)
            {
                var parent = roots.FirstOrDefault(m => m.Options.Contains(opt)); if (parent == null) return false; var col = parent.Options;
                ReorderCollection(col.Cast<object>().ToList(), sameLevel, targetItem, list => ApplyOrder(col, list.Cast<OptionItem>().ToList()));
                if (col.Any(o => o.Enabled == EnabledState.Enabled && (sameLevel.Contains(o) || ReferenceEquals(o, opt))) || parent.Enabled == EnabledState.Enabled) needPatch = true;
            }
            else if (targetItem is SubOptionItem sub)
            {
                var parentOpt = roots.SelectMany(m => m.Options).FirstOrDefault(o => o.SubOptions.Contains(sub)); if (parentOpt == null) return false; var col = parentOpt.SubOptions;
                ReorderCollection(col.Cast<object>().ToList(), sameLevel, targetItem, list => ApplyOrder(col, list.Cast<SubOptionItem>().ToList()));
                if (col.Any(s => s.Enabled == EnabledState.Enabled && (sameLevel.Contains(s) || ReferenceEquals(s, sub))) || parentOpt.Enabled == EnabledState.Enabled) needPatch = true;
            }
            return needPatch;
        }

        private static void ReorderCollection(List<object> snapshot, IEnumerable<object> draggedItems, object targetItem, Action<List<object>> apply)
        {
            var orderedDragged = draggedItems.Distinct().OrderBy(d => snapshot.IndexOf(d)).ToList();
            if (!orderedDragged.Any()) return;
            int targetIndex = snapshot.IndexOf(targetItem);
            if (targetIndex < 0) return;
            bool targetIncluded = orderedDragged.Contains(targetItem);
            snapshot = snapshot.Where(o => !orderedDragged.Contains(o)).ToList();
            int insertAt = targetIncluded ? targetIndex : (targetIndex > snapshot.Count ? snapshot.Count : targetIndex);
            snapshot.InsertRange(insertAt, orderedDragged);
            apply(snapshot);
        }
        private static void ApplyOrder<T>(IList<T> collection, List<T> newOrder)
        { collection.Clear(); foreach (var item in newOrder) collection.Add(item); }
    }
}
