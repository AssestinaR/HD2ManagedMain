using System.Collections.ObjectModel;
using System.Linq;
using HD2ModManager.Models;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    public class LibraryPageViewModel : PageViewModel
    {
        private readonly ModLibraryService _library;
        private readonly TagCatalogService _tags;

        public ObservableCollection<SectionViewModel> Sections { get; } = new();

        private string _query = string.Empty;
        public string Query { get => _query; set { _query = value; Refresh(); } }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand RemoveModCommand { get; }
        private bool _isCompact = true;
        public bool IsCompact { get => _isCompact; set { _isCompact = value; OnPropertyChanged(nameof(IsCompact)); } }

        public LibraryPageViewModel(ModLibraryService library)
        {
            Title = "Library";
            _library = library;
            _tags = TagCatalogService.Instance;
            RefreshCommand = new RelayCommand(Refresh);
            RemoveModCommand = new RelayCommand(() => { /* parameter passed via CommandParameter not used here */ });
            Refresh();
        }

        public void Refresh()
        {
            Sections.Clear();
            var all = _library.All().ToList();
            var q = (_query ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                all = all.Where(m =>
                    (m.Name?.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (m.Description?.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (m.Tags?.Any(t => t.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) ?? false)).ToList();
            }

            // Build sections driven by TagCatalogService: top-level where Parent==null
            var tagAll = _tags.GetAll();
            var topLevels = tagAll.Where(t => string.IsNullOrWhiteSpace(t.Parent)).Select(t => t.Name).Distinct().ToList();
            var sectionMap = new System.Collections.Generic.Dictionary<string, SectionViewModel>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var top in topLevels)
            {
                var sec = new SectionViewModel(top);
                sectionMap[top] = sec;
                // children: Parent==top
                var children = tagAll.Where(t => string.Equals(t.Parent, top, System.StringComparison.OrdinalIgnoreCase)).Select(t => t.Name).Distinct().ToList();
                foreach (var child in children)
                {
                    sec.Subsections.Add(new SubsectionViewModel(child));
                }
            }

            // Fallback section for unclassified
            var others = sectionMap.ContainsKey("其他") ? sectionMap["其他"] : new SectionViewModel("其他");
            if (!sectionMap.ContainsKey("其他")) sectionMap["其他"] = others;
            if (others.Subsections.Count == 0) others.Subsections.Add(new SubsectionViewModel("未分类"));

            // Build a fast lookup of tag keys -> TagItem
            var tagIndex = new System.Collections.Generic.Dictionary<string, Services.TagCatalogService.TagItem>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var ti in tagAll)
            {
                if (!string.IsNullOrWhiteSpace(ti.Name)) tagIndex[ti.Name] = ti;
                if (!string.IsNullOrWhiteSpace(ti.Code)) tagIndex[ti.Code] = ti;
                if (!string.IsNullOrWhiteSpace(ti.EnglishName)) tagIndex[ti.EnglishName] = ti;
                if (!string.IsNullOrWhiteSpace(ti.ChineseName)) tagIndex[ti.ChineseName] = ti;
            }

            // Build staging map: subsection -> list of cards (to be added in batches)
            var staging = new System.Collections.Generic.Dictionary<SubsectionViewModel, System.Collections.Generic.List<ModCardViewModel>>();
            void Enqueue(SubsectionViewModel sub, ModEntity m)
            {
                if (!staging.TryGetValue(sub, out var list)) { list = new System.Collections.Generic.List<ModCardViewModel>(); staging[sub] = list; }
                list.Add(new ModCardViewModel(m));
            }
            foreach (var m in all)
            {
                var placed = false;
                foreach (var tag in m.Tags)
                {
                    if (!tagIndex.TryGetValue(tag, out var ti)) continue;
                    var top = !string.IsNullOrWhiteSpace(ti.Category) ? ti.Category : (string.IsNullOrWhiteSpace(ti.Parent) ? ti.Name : string.Empty);
                    var child = !string.IsNullOrWhiteSpace(ti.Parent) ? ti.Parent : ti.Name;
                    if (!string.IsNullOrWhiteSpace(top) && sectionMap.TryGetValue(top!, out var sec))
                    {
                        var sub = sec.Subsections.FirstOrDefault(s => string.Equals(s.Title, child, System.StringComparison.OrdinalIgnoreCase));
                        if (sub == null)
                        {
                            if (sec.Subsections.Count == 0) sec.Subsections.Add(new SubsectionViewModel("未分类"));
                            sub = sec.Subsections.FirstOrDefault();
                        }
                        Enqueue(sub!, m);
                        placed = true; break;
                    }
                }
                if (!placed)
                {
                    var def = others.Subsections.FirstOrDefault() ?? new SubsectionViewModel("未分类");
                    if (!others.Subsections.Contains(def)) others.Subsections.Add(def);
                    Enqueue(def, m);
                }
            }

            // Append sections in a stable order (topLevels order, then 其他)
            foreach (var top in topLevels)
            {
                Sections.Add(sectionMap[top]);
            }
            if (!topLevels.Any(t => t == "其他")) Sections.Add(others);

            // Batched append to UI to avoid long blocking
            const int BatchSize = 30;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            foreach (var kv in staging)
            {
                var sub = kv.Key;
                var list = kv.Value;
                int idx = 0;
                void AppendBatch()
                {
                    int take = System.Math.Min(BatchSize, list.Count - idx);
                    for (int i = 0; i < take; i++) sub.Mods.Add(list[idx + i]);
                    idx += take;
                    sub.HasContent = sub.Mods.Count > 0;
                    if (idx < list.Count)
                    {
                        dispatcher?.BeginInvoke(new System.Action(AppendBatch), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                AppendBatch();
            }
            foreach (var sec in Sections)
            {
                sec.HasContent = sec.Subsections.Any(s => s.HasContent);
            }
        }

        private void RemoveMod(ModCardViewModel? card)
        {
            if (card == null) return;
            try
            {
                _library.Remove(card.Mod.Guid);
                _library.Save();
            }
            catch { }
            Refresh();
        }

        // Helpers removed: now driven by TagCatalogService
    }

    public class SectionViewModel
    {
        public string Title { get; }
        public ObservableCollection<SubsectionViewModel> Subsections { get; } = new();
        public bool HasContent { get; set; }
        public SectionViewModel(string title) { Title = title; }
    }

    public class SubsectionViewModel
    {
        public string Title { get; }
        public ObservableCollection<ModCardViewModel> Mods { get; } = new();
        public bool HasContent { get; set; }
        public SubsectionViewModel(string title) { Title = title; }
    }

    public class ModCardViewModel
    {
        public HD2ModManager.Models.ModEntity Mod { get; }
        public string Name => Mod.Name;
        public string TagsString => string.Join(", ", Mod.Tags ?? new System.Collections.Generic.List<string>());
        public string ArmorInfo { get; }
        public ModCardViewModel(HD2ModManager.Models.ModEntity mod)
        {
            Mod = mod;
            ArmorInfo = BuildArmorInfo(mod);
        }

        private static string BuildArmorInfo(HD2ModManager.Models.ModEntity mod)
        {
            var tags = mod.Tags ?? new System.Collections.Generic.List<string>();
            var catalog = HD2ModManager.Services.TagCatalogService.Instance;
            foreach (var t in tags)
            {
                var ti = catalog.GetAll().FirstOrDefault(x => x.Name == t || x.Code == t);
                if (ti != null && ti.Category == "护甲")
                {
                    var name = ti.Name;
                    var passive = string.Empty;
                    if (!string.IsNullOrWhiteSpace(ti.PassiveEnglish) || !string.IsNullOrWhiteSpace(ti.PassiveChinese))
                    {
                        passive = $"{ti.PassiveEnglish} {ti.PassiveChinese}".Trim();
                    }
                    var desc = string.Empty;
                    if (!string.IsNullOrWhiteSpace(ti.PassiveDescEnglish) || !string.IsNullOrWhiteSpace(ti.PassiveDescChinese))
                    {
                        desc = $"{ti.PassiveDescEnglish}\n{ti.PassiveDescChinese}".Trim();
                    }
                    var stats = $"Armor: {ti.Armor?.ToString() ?? "-"}  Speed: {ti.Speed?.ToString() ?? "-"}  Stamina: {ti.Stamina?.ToString() ?? "-"}";
                    return string.Join("\n", new[] { name, passive, desc, stats }.Where(s => !string.IsNullOrWhiteSpace(s)));
                }
            }
            return string.Empty;
        }
    }

}
