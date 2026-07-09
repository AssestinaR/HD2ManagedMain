using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using HD2ModManager.Models;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    // 作用：根据标签目录把模组列表构造成库页的分区/子分区结构。
    internal sealed class LibrarySectionBuilder
    {
        private const int BatchSize = 30;
        private readonly TagCatalogService _tags;
        private readonly Func<string, bool> _isSelected;

        public LibrarySectionBuilder(TagCatalogService tags, Func<string, bool> isSelected)
        {
            _tags = tags;
            _isSelected = isSelected;
        }

        public void Rebuild(ObservableCollection<SectionViewModel> target, IEnumerable<ModEntity> mods)
        {
            target.Clear();
            var tagAll = _tags.GetAll().ToList();
            var topLevels = tagAll.Where(t => string.IsNullOrWhiteSpace(t.Parent)).Select(t => t.Name).Distinct().ToList();
            var sectionMap = BuildSectionMap(tagAll, topLevels);
            var others = EnsureFallbackSection(sectionMap);
            var tagIndex = BuildTagIndex(tagAll);
            var staging = StageCards(mods, sectionMap, others, tagIndex);

            foreach (var top in topLevels)
            {
                target.Add(sectionMap[top]);
            }
            if (!topLevels.Any(t => string.Equals(t, "其他", StringComparison.OrdinalIgnoreCase))) target.Add(others);

            AppendBatched(staging);
            foreach (var section in target)
            {
                section.HasContent = section.Subsections.Any(s => s.HasContent);
            }
        }

        private static Dictionary<string, SectionViewModel> BuildSectionMap(IReadOnlyList<TagCatalogService.TagItem> tagAll, IReadOnlyList<string> topLevels)
        {
            var sectionMap = new Dictionary<string, SectionViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var top in topLevels)
            {
                var section = new SectionViewModel(top);
                sectionMap[top] = section;
                var children = tagAll.Where(t => string.Equals(t.Parent, top, StringComparison.OrdinalIgnoreCase)).Select(t => t.Name).Distinct();
                foreach (var child in children)
                {
                    section.Subsections.Add(new SubsectionViewModel(child));
                }
            }
            return sectionMap;
        }

        private static SectionViewModel EnsureFallbackSection(Dictionary<string, SectionViewModel> sectionMap)
        {
            var others = sectionMap.TryGetValue("其他", out var existing) ? existing : new SectionViewModel("其他");
            sectionMap.TryAdd("其他", others);
            if (others.Subsections.Count == 0) others.Subsections.Add(new SubsectionViewModel("未分类"));
            return others;
        }

        private static Dictionary<string, TagCatalogService.TagItem> BuildTagIndex(IEnumerable<TagCatalogService.TagItem> tagAll)
        {
            var tagIndex = new Dictionary<string, TagCatalogService.TagItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in tagAll)
            {
                if (!string.IsNullOrWhiteSpace(item.Name)) tagIndex[item.Name] = item;
                if (!string.IsNullOrWhiteSpace(item.Code)) tagIndex[item.Code] = item;
                if (!string.IsNullOrWhiteSpace(item.EnglishName)) tagIndex[item.EnglishName] = item;
                if (!string.IsNullOrWhiteSpace(item.ChineseName)) tagIndex[item.ChineseName] = item;
            }
            return tagIndex;
        }

        private Dictionary<SubsectionViewModel, List<ModCardViewModel>> StageCards(
            IEnumerable<ModEntity> mods,
            Dictionary<string, SectionViewModel> sectionMap,
            SectionViewModel others,
            Dictionary<string, TagCatalogService.TagItem> tagIndex)
        {
            var staging = new Dictionary<SubsectionViewModel, List<ModCardViewModel>>();
            foreach (var mod in mods)
            {
                var subsection = ResolveSubsection(mod, sectionMap, others, tagIndex);
                if (!staging.TryGetValue(subsection, out var list))
                {
                    list = new List<ModCardViewModel>();
                    staging[subsection] = list;
                }
                list.Add(new ModCardViewModel(mod, _isSelected(mod.Guid)));
                subsection.HasContent = true;
            }
            return staging;
        }

        private static SubsectionViewModel ResolveSubsection(
            ModEntity mod,
            Dictionary<string, SectionViewModel> sectionMap,
            SectionViewModel others,
            Dictionary<string, TagCatalogService.TagItem> tagIndex)
        {
            foreach (var tag in mod.Tags)
            {
                if (!tagIndex.TryGetValue(tag, out var tagItem)) continue;
                var top = !string.IsNullOrWhiteSpace(tagItem.Category) ? tagItem.Category : (string.IsNullOrWhiteSpace(tagItem.Parent) ? tagItem.Name : string.Empty);
                var child = !string.IsNullOrWhiteSpace(tagItem.Parent) ? tagItem.Parent : tagItem.Name;
                if (string.IsNullOrWhiteSpace(top) || !sectionMap.TryGetValue(top, out var section)) continue;
                var subsection = section.Subsections.FirstOrDefault(s => string.Equals(s.Title, child, StringComparison.OrdinalIgnoreCase));
                if (subsection != null) return subsection;
                if (section.Subsections.Count == 0) section.Subsections.Add(new SubsectionViewModel("未分类"));
                return section.Subsections.First();
            }

            var fallback = others.Subsections.FirstOrDefault() ?? new SubsectionViewModel("未分类");
            if (!others.Subsections.Contains(fallback)) others.Subsections.Add(fallback);
            return fallback;
        }

        private static void AppendBatched(Dictionary<SubsectionViewModel, List<ModCardViewModel>> staging)
        {
            var dispatcher = Application.Current?.Dispatcher;
            foreach (var (subsection, list) in staging)
            {
                var index = 0;
                void AppendBatch()
                {
                    var take = Math.Min(BatchSize, list.Count - index);
                    for (var i = 0; i < take; i++) subsection.Mods.Add(list[index + i]);
                    index += take;
                    subsection.HasContent = subsection.Mods.Count > 0;
                    if (index < list.Count)
                    {
                        dispatcher?.BeginInvoke(new Action(AppendBatch), DispatcherPriority.Background);
                    }
                }
                AppendBatch();
            }
        }
    }
}
