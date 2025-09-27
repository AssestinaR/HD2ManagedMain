using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ManagedMain.Models
{
    public class ManagedMainOptions : INotifyPropertyChanged
    {
        private string _gameFolder = string.Empty;
        public string GameFolder
        {
            get => _gameFolder;
            set { if (_gameFolder != value) { _gameFolder = value; OnPropertyChanged(); } }
        }

        private List<ProfileEntry> _profiles = new();
        public List<ProfileEntry> Profiles
        {
            get => _profiles;
            set { if (!ReferenceEquals(_profiles, value)) { _profiles = value ?? new(); OnPropertyChanged(); } }
        }

        private Guid? _lastFocusedProfileId;
        public Guid? LastFocusedProfileId
        {
            get => _lastFocusedProfileId;
            set { if (_lastFocusedProfileId != value) { _lastFocusedProfileId = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ProfileEntry : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private string _name = string.Empty;
        private string _rootPath = string.Empty;
        private bool _isOpen;
        private bool _isEnabled;

        public Guid Id { get => _id; set { if (_id != value) { _id = value; OnPropertyChanged(); } } }
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }
        public string RootPath { get => _rootPath; set { if (_rootPath != value) { _rootPath = value; OnPropertyChanged(); } } }
        public bool IsOpen { get => _isOpen; set { if (_isOpen != value) { _isOpen = value; OnPropertyChanged(); } } }
        public bool IsEnabled { get => _isEnabled; set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } } }

        [JsonIgnore]
        public ObservableCollection<MainModItem> Mods { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
