using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HD2ModManager.ViewModels
{
    // 作用：维护跨页面唯一的临时选择上下文，供底部悬浮操作栏使用。
    public sealed class SelectionCoordinator : BaseViewModel
    {
        private string? _scope;
        private readonly ObservableCollection<string> _selectedIds = new();

        public event EventHandler? SelectionChanged;

        public string? Scope
        {
            get => _scope;
            private set
            {
                if (SetField(ref _scope, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public IReadOnlyList<string> SelectedIds => _selectedIds;
        public bool HasSelection => _selectedIds.Count > 0;
        public string Summary => Scope switch
        {
            "Library" => $"已选择 {_selectedIds.Count} 个库内 Mod",
            "Profile" => $"已选择 {_selectedIds.Count} 个配置项",
            var scope when scope?.StartsWith("DecorationHost:", StringComparison.Ordinal) == true => $"已选择 {_selectedIds.Count} 个装饰 Mod",
            _ => $"已选择 {_selectedIds.Count} 项",
        };

        public void Replace(string scope, IEnumerable<string> ids)
        {
            Scope = scope;
            _selectedIds.Clear();
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _selectedIds.Add(id);
            }
            if (_selectedIds.Count == 0) Scope = null;
            NotifySelectionChanged();
        }

        public void Clear()
        {
            _selectedIds.Clear();
            Scope = null;
            NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedIds));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(Summary));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
