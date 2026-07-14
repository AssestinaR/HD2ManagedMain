using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModManager.Services
{
    // 作用：为 Manager 暴露基于 HD2ModCore Profile 语义的配置服务。
    public sealed class ProfileService
    {
        private readonly HD2ModCore.Application.IModLibraryManager _manager;
        private LibrarySnapshot _snapshot;
        private readonly Dictionary<string, ProfileId> _profileIds = new(StringComparer.OrdinalIgnoreCase);
        private string? _selectedProfileName;

        public ProfileService(string profilesPath)
        {
            var paths = SettingsService.CreateStoragePaths();
            _manager = CoreServices.CreateModLibraryManager(paths);
            _snapshot = EmptySnapshot();
        }

        public LibrarySnapshot Snapshot => _snapshot;
        public IReadOnlyList<Profile> Profiles => _snapshot.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        public string? SelectedKey => _selectedProfileName;
        public Profile? SelectedProfile => SelectedProfileId is ProfileId id ? _snapshot.Profiles.FirstOrDefault(p => p.Id == id) : null;
        public ProfileId? SelectedProfileId => !string.IsNullOrWhiteSpace(_selectedProfileName) && _profileIds.TryGetValue(_selectedProfileName, out var id) ? id : null;
        public string? ActiveKey => ActiveProfile?.Name;
        public Profile? ActiveProfile => _snapshot.ActiveProfileId is ProfileId id ? _snapshot.Profiles.FirstOrDefault(p => p.Id == id) : null;
        public Profile? ActiveCoreProfile => ActiveProfile;
        public ProfileId? ActiveProfileId => _snapshot.ActiveProfileId;
        public event EventHandler? ActiveProfileDeploymentRequired;
        public event EventHandler? ActiveProfileDeactivationRequired;
        public event EventHandler? Changed;

        public void Load()
        {
            _snapshot = _manager.LoadOrCreateAsync().AsTask().GetAwaiter().GetResult();
            RebuildIndex();
        }

        public IReadOnlyList<Profile> All() => Profiles;

        public string CreateNew(string? requestedName = null)
        {
            var name = CreateUniqueName(requestedName);
            var profile = new Profile(ProfileId.New(), name, DateTimeOffset.UtcNow, null, Array.Empty<ProfileEntry>());
            _snapshot = _manager.UpsertProfileAsync(profile).AsTask().GetAwaiter().GetResult();
            _selectedProfileName = name;
            SettingsService.SetSelectedProfileKey(_selectedProfileName);
            RebuildIndex();
            Changed?.Invoke(this, EventArgs.Empty);
            return name;
        }

        public bool Remove(string name)
        {
            if (!_profileIds.TryGetValue(name, out var id)) return false;
            _snapshot = _manager.DeleteProfileAsync(id).AsTask().GetAwaiter().GetResult();
            if (string.Equals(_selectedProfileName, name, StringComparison.OrdinalIgnoreCase))
            {
                _selectedProfileName = _snapshot.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Name;
                SettingsService.SetSelectedProfileKey(_selectedProfileName);
            }
            RebuildIndex();
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Select(string name)
        {
            if (_profileIds.ContainsKey(name))
            {
                _selectedProfileName = name;
                SettingsService.SetSelectedProfileKey(name);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool ActivateSelected()
        {
            if (SelectedProfileId is not ProfileId profileId) return false;
            _snapshot = _manager.SetActiveProfileAsync(profileId).AsTask().GetAwaiter().GetResult();
            ActiveProfileDeploymentRequired?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool DisableActive()
        {
            if (_snapshot.ActiveProfileId is null) return false;
            _snapshot = _manager.SetActiveProfileAsync(null).AsTask().GetAwaiter().GetResult();
            ActiveProfileDeactivationRequired?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Rename(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return false;
            if (!_profileIds.TryGetValue(oldName, out var id)) return false;

            try
            {
                _snapshot = _manager.RenameProfileAsync(id, newName).AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }

            var normalized = newName.Trim();
            if (string.Equals(_selectedProfileName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _selectedProfileName = normalized;
                SettingsService.SetSelectedProfileKey(normalized);
            }
            RebuildIndex();
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool AddModToSelected(string nodeGuid)
        {
            if (SelectedProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            _snapshot = _manager.AddProfileEntryAsync(profileId, nodeId).AsTask().GetAwaiter().GetResult();
            RebuildIndex();
            NotifyIfActive(profileId);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool RemoveModFromSelected(string nodeGuid)
        {
            if (SelectedProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            _snapshot = _manager.RemoveProfileEntryAsync(profileId, nodeId).AsTask().GetAwaiter().GetResult();
            RebuildIndex();
            NotifyIfActive(profileId);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool MoveModInSelected(string nodeGuid, int direction)
        {
            if (SelectedProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            _snapshot = _manager.MoveProfileEntryAsync(profileId, nodeId, direction).AsTask().GetAwaiter().GetResult();
            RebuildIndex();
            NotifyIfActive(profileId);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public IReadOnlyList<ProfileEntry> GetSortedEntries(Profile profile)
        {
            return profile.Entries.OrderBy(e => e.LoadOrder).ThenBy(e => e.AddedUtc).ToList();
        }

        private void RebuildIndex()
        {
            _profileIds.Clear();
            foreach (var profile in _snapshot.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                _profileIds[profile.Name] = profile.Id;
            }

            var persisted = SettingsService.GetSelectedProfileKey();
            if (!string.IsNullOrWhiteSpace(persisted) && _profileIds.ContainsKey(persisted))
            {
                _selectedProfileName = persisted;
            }
            else if (_selectedProfileName == null || !_profileIds.ContainsKey(_selectedProfileName))
            {
                _selectedProfileName = _profileIds.Keys.FirstOrDefault();
                SettingsService.SetSelectedProfileKey(_selectedProfileName);
            }
        }

        public void NotifyActiveModContentChanged()
        {
            if (_snapshot.ActiveProfileId is not null)
            {
                ActiveProfileDeploymentRequired?.Invoke(this, EventArgs.Empty);
            }
        }

        private void NotifyIfActive(ProfileId profileId)
        {
            if (_snapshot.ActiveProfileId == profileId)
            {
                ActiveProfileDeploymentRequired?.Invoke(this, EventArgs.Empty);
            }
        }

        private string CreateUniqueName(string? requestedName)
        {
            var baseName = string.IsNullOrWhiteSpace(requestedName) ? "Profile" : requestedName.Trim();
            if (!_profileIds.ContainsKey(baseName)) return baseName;

            var index = 2;
            string candidate;
            do
            {
                candidate = $"{baseName} {index}";
                index++;
            }
            while (_profileIds.ContainsKey(candidate));
            return candidate;
        }

        private static bool TryParseNodeId(string nodeGuid, out ModNodeId nodeId)
        {
            if (Guid.TryParse(nodeGuid, out var guid))
            {
                nodeId = new ModNodeId(guid);
                return true;
            }

            nodeId = default;
            return false;
        }

        private static LibrarySnapshot EmptySnapshot() => new(
            Version: 1,
            SavedUtc: DateTimeOffset.UtcNow,
            Nodes: new Dictionary<ModNodeId, ModNode>(),
            Profiles: Array.Empty<Profile>());
    }
}
