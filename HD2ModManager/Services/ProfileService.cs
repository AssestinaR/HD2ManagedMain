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
        private string? _activeProfileName;

        public ProfileService(string profilesPath)
        {
            var paths = new StoragePaths(AppDomain.CurrentDomain.BaseDirectory);
            _manager = CoreServices.CreateModLibraryManager(paths);
            _snapshot = EmptySnapshot();
        }

        public LibrarySnapshot Snapshot => _snapshot;
        public IReadOnlyList<Profile> Profiles => _snapshot.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        public string? ActiveKey => _activeProfileName;
        public Profile? ActiveProfile => ActiveProfileId is ProfileId id ? _snapshot.Profiles.FirstOrDefault(p => p.Id == id) : null;
        public Profile? ActiveCoreProfile => ActiveProfile;
        public ProfileId? ActiveProfileId => !string.IsNullOrWhiteSpace(_activeProfileName) && _profileIds.TryGetValue(_activeProfileName, out var id) ? id : null;

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
            _activeProfileName = name;
            SettingsService.SetActiveProfileKey(_activeProfileName);
            RebuildIndex();
            return name;
        }

        public bool Remove(string name)
        {
            if (!_profileIds.TryGetValue(name, out var id)) return false;
            _snapshot = _manager.DeleteProfileAsync(id).AsTask().GetAwaiter().GetResult();
            if (string.Equals(_activeProfileName, name, StringComparison.OrdinalIgnoreCase))
            {
                _activeProfileName = _snapshot.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Name;
                SettingsService.SetActiveProfileKey(_activeProfileName);
            }
            RebuildIndex();
            return true;
        }

        public void SetActive(string name)
        {
            if (_profileIds.ContainsKey(name))
            {
                _activeProfileName = name;
                SettingsService.SetActiveProfileKey(name);
            }
        }

        public bool DisableActive()
        {
            if (_activeProfileName == null) return false;
            _activeProfileName = null;
            SettingsService.SetActiveProfileKey(null);
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
            if (string.Equals(_activeProfileName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _activeProfileName = normalized;
                SettingsService.SetActiveProfileKey(normalized);
            }
            RebuildIndex();
            return true;
        }

        public bool AddModToActive(string nodeGuid)
        {
            if (ActiveProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            _snapshot = _manager.AddProfileEntryAsync(profileId, nodeId).AsTask().GetAwaiter().GetResult();
            RebuildIndex();
            return true;
        }

        public bool RemoveModFromActive(string nodeGuid)
        {
            if (ActiveProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            _snapshot = _manager.RemoveProfileEntryAsync(profileId, nodeId).AsTask().GetAwaiter().GetResult();
            RebuildIndex();
            return true;
        }

        public bool MoveModInActive(string nodeGuid, int direction)
        {
            if (ActiveProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            _snapshot = _manager.MoveProfileEntryAsync(profileId, nodeId, direction).AsTask().GetAwaiter().GetResult();
            RebuildIndex();
            return true;
        }

        public bool SetModEnabledInActive(string nodeGuid, bool enabled)
        {
            if (ActiveProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            _snapshot = _manager.SetProfileEntryEnabledAsync(profileId, nodeId, enabled).AsTask().GetAwaiter().GetResult();
            RebuildIndex();
            return true;
        }

        public int SetModsEnabledInActive(IEnumerable<string> nodeGuids, bool enabled)
        {
            var changed = 0;
            foreach (var guid in nodeGuids)
            {
                if (SetModEnabledInActive(guid, enabled)) changed++;
            }
            return changed;
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

            var persisted = SettingsService.GetActiveProfileKey();
            if (!string.IsNullOrWhiteSpace(persisted) && _profileIds.ContainsKey(persisted))
            {
                _activeProfileName = persisted;
            }
            else if (_activeProfileName == null || !_profileIds.ContainsKey(_activeProfileName))
            {
                _activeProfileName = _profileIds.Keys.FirstOrDefault();
                SettingsService.SetActiveProfileKey(_activeProfileName);
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
