using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.Json.Serialization;

namespace LiberTeaManager
{
    public class SubOptionItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private string _name;
        private string _description;
        private bool _isSelected;
        [JsonConverter(typeof(EnabledStateJsonConverter))]
        private EnabledState _enabled; // Disabled / Enabled (Sub 不出现 Partial, 但保持枚举兼容)
        private string _image;
        private string _iconPath;
        private string _rootModName;
        private string _url;
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }
        public string Description { get => _description; set { if (_description != value) { _description = value; OnPropertyChanged(); } } }
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } } }
        public EnabledState Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } } }
        public string Image { get => _image; set { if (_image != value) { _image = value; OnPropertyChanged(); } } }
        public string IconPath { get => _iconPath; set { if (_iconPath != value) { _iconPath = value; OnPropertyChanged(); } } }
        public string RootModName { get => _rootModName; set { if (_rootModName != value) { _rootModName = value; OnPropertyChanged(); } } }
        public string Url { get => _url; set { if (_url != value) { _url = value; OnPropertyChanged(); } } }
        public List<ModFileGroup> FileGroups { get; set; } = new();
        public List<string> Include { get; set; } = new();
        public int TotalFileGroupCount => FileGroups.Count;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class OptionItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private string _name;
        private string _description;
        private bool _isSelected;
        [JsonConverter(typeof(EnabledStateJsonConverter))]
        private EnabledState _enabled; // Disabled / Enabled / Partial
        private string _image;
        private string _iconPath;
        private string _rootModName;
        private string _url;
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }
        public string Description { get => _description; set { if (_description != value) { _description = value; OnPropertyChanged(); } } }
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } } }
        public EnabledState Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } } }
        public string Image { get => _image; set { if (_image != value) { _image = value; OnPropertyChanged(); } } }
        public string IconPath { get => _iconPath; set { if (_iconPath != value) { _iconPath = value; OnPropertyChanged(); } } }
        public string RootModName { get => _rootModName; set { if (_rootModName != value) { _rootModName = value; OnPropertyChanged(); } } }
        public string Url { get => _url; set { if (_url != value) { _url = value; OnPropertyChanged(); } } }
        public List<ModFileGroup> FileGroups { get; set; } = new();
        public ObservableCollection<SubOptionItem> SubOptions { get; set; } = new();
        public List<string> Include { get; set; } = new();
        public int TotalFileGroupCount => FileGroups.Count + SubOptions.Sum(s => s.FileGroups?.Count ?? 0);
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MainModItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private string _name;
        private string _description;
        private Guid _guid;
        private bool _isSelected;
        [JsonConverter(typeof(EnabledStateJsonConverter))]
        private EnabledState _enabled; // Disabled / Enabled / Partial
        private string _image;
        private string _iconPath;
        private string _rootModName;
        private string _url;
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }
        public string Description { get => _description; set { if (_description != value) { _description = value; OnPropertyChanged(); } } }
        public Guid Guid { get => _guid; set { if (_guid != value) { _guid = value; OnPropertyChanged(); } } }
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } } }
        public EnabledState Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } } }
        public string Image { get => _image; set { if (_image != value) { _image = value; OnPropertyChanged(); } } }
        public string IconPath { get => _iconPath; set { if (_iconPath != value) { _iconPath = value; OnPropertyChanged(); } } }
        public string RootModName { get => _rootModName; set { if (_rootModName != value) { _rootModName = value; OnPropertyChanged(); } } }
        public string Url { get => _url; set { if (_url != value) { _url = value; OnPropertyChanged(); } } }
        public List<ModFileGroup> FileGroups { get; set; } = new();
        public ObservableCollection<OptionItem> Options { get; set; } = new();
        public int TotalFileGroupCount => FileGroups.Count + Options.Sum(o => (o.FileGroups?.Count ?? 0) + o.SubOptions.Sum(s => s.FileGroups?.Count ?? 0));
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ModFileGroup
    {
        public string RelativePath { get; set; }
        public string HexPrefix { get; set; }
        public int PatchN { get; set; }
        public List<string> Files { get; set; } = new();
    }
}