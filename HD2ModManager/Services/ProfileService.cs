using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using System.Diagnostics;

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

        public void ReloadFromLibrary()
        {
            Load();
            Changed?.Invoke(this, EventArgs.Empty);
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

        public async Task<bool> AddModToSelectedAsync(string nodeGuid, CancellationToken cancellationToken = default)
        {
            if (SelectedProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            var stopwatch = Stopwatch.StartNew();
            LogService.Info($"配置性能：开始加入配置。Profile={profileId.Value:N}，Mod={nodeId.Value:N}。");
            var snapshot = await Task.Run(
                () => _manager.AddProfileEntryAsync(profileId, nodeId, cancellationToken).AsTask(),
                cancellationToken).ConfigureAwait(false);
            LogService.Info($"配置性能：加入配置 Core 写入完成，耗时 {stopwatch.ElapsedMilliseconds}ms。Profile={profileId.Value:N}，Mod={nodeId.Value:N}。");
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                await dispatcher.InvokeAsync(() => ApplyAddedProfileSnapshot(snapshot, profileId));
            }
            else
            {
                ApplyAddedProfileSnapshot(snapshot, profileId);
            }
            LogService.Info($"配置性能：加入配置 UI 快照提交完成，总耗时 {stopwatch.ElapsedMilliseconds}ms。Profile={profileId.Value:N}，Mod={nodeId.Value:N}。");
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

        public async Task<bool> RemoveModFromSelectedAsync(string nodeGuid, CancellationToken cancellationToken = default)
        {
            if (SelectedProfileId is not ProfileId profileId || !TryParseNodeId(nodeGuid, out var nodeId)) return false;
            var stopwatch = Stopwatch.StartNew();
            LogService.Info($"配置性能：开始从配置移除。Profile={profileId.Value:N}，Mod={nodeId.Value:N}。");
            var snapshot = await Task.Run(
                () => _manager.RemoveProfileEntryAsync(profileId, nodeId, cancellationToken).AsTask(),
                cancellationToken).ConfigureAwait(false);
            LogService.Info($"配置性能：配置移除 Core 写入完成，耗时 {stopwatch.ElapsedMilliseconds}ms。Profile={profileId.Value:N}，Mod={nodeId.Value:N}。");
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                await dispatcher.InvokeAsync(() => ApplyAddedProfileSnapshot(snapshot, profileId));
            }
            else
            {
                ApplyAddedProfileSnapshot(snapshot, profileId);
            }
            LogService.Info($"配置性能：配置移除 UI 快照提交完成，总耗时 {stopwatch.ElapsedMilliseconds}ms。Profile={profileId.Value:N}，Mod={nodeId.Value:N}。");
            return true;
        }

        public async Task<bool> RemoveModsFromSelectedAsync(IReadOnlyList<string> nodeGuids, CancellationToken cancellationToken = default)
        {
            if (nodeGuids.Count == 0 || SelectedProfileId is not ProfileId profileId) return false;
            var ids = nodeGuids.Where(guid => TryParseNodeId(guid, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ids.Count == 0) return false;
            LibrarySnapshot snapshot = _snapshot;
            foreach (var guid in ids)
            {
                if (!TryParseNodeId(guid, out var nodeId)) continue;
                snapshot = await Task.Run(() => _manager.RemoveProfileEntryAsync(profileId, nodeId, cancellationToken).AsTask(), cancellationToken).ConfigureAwait(false);
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            Action apply = () =>
            {
                _snapshot = snapshot;
                RebuildIndex();
                NotifyIfActive(profileId);
                Changed?.Invoke(this, EventArgs.Empty);
            };
            if (dispatcher is not null && !dispatcher.CheckAccess()) await dispatcher.InvokeAsync(apply);
            else apply();
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

        // Purpose: Apply a complete ordered Profile membership atomically.
        public bool ReplaceSelectedEntries(IReadOnlyList<string> nodeGuids)
        {
            if (SelectedProfileId is not ProfileId profileId) return false;
            var ids = nodeGuids.Select(guid => TryParseNodeId(guid, out var nodeId) ? (ModNodeId?)nodeId : null).Where(id => id.HasValue).Select(id => id!.Value).ToList();
            var profile = _snapshot.Profiles.FirstOrDefault(item => item.Id == profileId);
            if (profile is null) return false;
            var entries = ids.Select((id, index) =>
            {
                var existing = profile.Entries.FirstOrDefault(entry => entry.NodeId == id);
                return existing is null ? new ProfileEntry(id, index) : existing with { LoadOrder = index };
            }).ToList();
            _snapshot = _manager.UpsertProfileAsync(profile with { Entries = entries, ModifiedUtc = DateTimeOffset.UtcNow, Revision = checked(profile.Revision + 1) }).AsTask().GetAwaiter().GetResult();
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

        private void ApplyAddedProfileSnapshot(LibrarySnapshot snapshot, ProfileId profileId)
        {
            _snapshot = snapshot;
            RebuildIndex();
            NotifyIfActive(profileId);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
