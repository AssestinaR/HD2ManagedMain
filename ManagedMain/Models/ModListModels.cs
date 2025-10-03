using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.Json.Serialization;

namespace ManagedMain.Models
{
    public class ModFileGroup
    {
        public string HexPrefix { get; set; } = string.Empty;
        public int PatchN { get; set; }
        public List<string> Files { get; set; } = new();
        public string RelativePath { get; set; } = string.Empty;
    }

    public abstract class NotifyObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    public class SubOptionItem : NotifyObject
    {
        private string _name = string.Empty;
        public string Name { get => _name; set => SetField(ref _name, value); }

        private string? _description;
        public string? Description { get => _description; set => SetField(ref _description, value); }

        private int _enabled = 0; // 0/1
        public int Enabled { get => _enabled; set => SetField(ref _enabled, value); }

        private string? _image;
        public string? Image { get => _image; set => SetField(ref _image, value); }

        private string? _iconPath;
        public string? IconPath { get => _iconPath; set => SetField(ref _iconPath, value); }

        private string? _url;
        public string? Url { get => _url; set => SetField(ref _url, value); }

        public List<string> Include { get; set; } = new();
        public List<ModFileGroup> FileGroups { get; set; } = new();

        [JsonIgnore]
        public int TotalFileGroupCount => (FileGroups?.Count ?? 0);

        [JsonIgnore]
        public IEnumerable<ModFileGroup> AllFileGroups => FileGroups ?? Enumerable.Empty<ModFileGroup>();

        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
    }

    public class OptionItem : NotifyObject
    {
        private string _name = string.Empty;
        public string Name { get => _name; set => SetField(ref _name, value); }

        private string? _description;
        public string? Description { get => _description; set => SetField(ref _description, value); }

        private int _enabled = 0; // 0/1/2
        public int Enabled { get => _enabled; set => SetField(ref _enabled, value); }

        private string? _image;
        public string? Image { get => _image; set => SetField(ref _image, value); }

        private string? _iconPath;
        public string? IconPath { get => _iconPath; set => SetField(ref _iconPath, value); }

        private string? _url;
        public string? Url { get => _url; set => SetField(ref _url, value); }

        // New: whether sub options are single-select
        private bool _subOptionsSingleSelect;
        public bool SubOptionsSingleSelect { get => _subOptionsSingleSelect; set => SetField(ref _subOptionsSingleSelect, value); }

        public List<string> Include { get; set; } = new();
        public List<ModFileGroup> FileGroups { get; set; } = new();
        public ObservableCollection<SubOptionItem> SubOptions { get; set; } = new();

        [JsonIgnore]
        public int TotalFileGroupCount => (FileGroups?.Count ?? 0) + (SubOptions?.Sum(s => s.TotalFileGroupCount) ?? 0);

        [JsonIgnore]
        public IEnumerable<ModFileGroup> AllFileGroups
            => (FileGroups ?? Enumerable.Empty<ModFileGroup>())
               .Concat(SubOptions?.SelectMany(s => s.AllFileGroups) ?? Enumerable.Empty<ModFileGroup>());

        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
    }

    public class MainModItem : NotifyObject
    {
        private Guid _guid = Guid.NewGuid();
        public Guid Guid { get => _guid; set => SetField(ref _guid, value); }

        private string _name = string.Empty;
        public string Name { get => _name; set => SetField(ref _name, value); }

        private string? _description;
        public string? Description { get => _description; set => SetField(ref _description, value); }

        private int _enabled = 0; // 0/1/2
        public int Enabled { get => _enabled; set => SetField(ref _enabled, value); }

        private string? _image;
        public string? Image { get => _image; set => SetField(ref _image, value); }

        private string? _iconPath;
        public string? IconPath { get => _iconPath; set => SetField(ref _iconPath, value); }

        private string? _url;
        public string? Url { get => _url; set => SetField(ref _url, value); }

        // New: whether options are single-select
        private bool _optionsSingleSelect;
        public bool OptionsSingleSelect { get => _optionsSingleSelect; set => SetField(ref _optionsSingleSelect, value); }

        public List<ModFileGroup> FileGroups { get; set; } = new();
        public ObservableCollection<OptionItem> Options { get; set; } = new();

        [JsonIgnore]
        public int TotalFileGroupCount => (FileGroups?.Count ?? 0) + (Options?.Sum(o => o.TotalFileGroupCount) ?? 0);

        [JsonIgnore]
        public IEnumerable<ModFileGroup> AllFileGroups
            => (FileGroups ?? Enumerable.Empty<ModFileGroup>())
               .Concat(Options?.SelectMany(o => o.AllFileGroups) ?? Enumerable.Empty<ModFileGroup>());

        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
    }
}
