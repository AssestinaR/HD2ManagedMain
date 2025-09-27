using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ManagedMain.Models;
using ManagedMain.Services;
using System.Threading.Tasks;

namespace ManagedMain.ViewModels
{
    public class ManagedMainViewModel : INotifyPropertyChanged
    {
        private readonly OptionStore _optionStore;
        private readonly ILogService _log;
        private readonly Action<ProfileEntry>? _openProfileTab;
        private readonly ModListStatsService _statsService = new();
        private readonly ModListStore _modListStore = new();
        private readonly ActivationService _activation = new();

        public ObservableCollection<ProfileEntry> Profiles { get; }
        public ManagedMainOptions Options { get; }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        private bool _isNewProfileRunning;
        public bool IsNewProfileRunning { get => _isNewProfileRunning; set { _isNewProfileRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(SurgeNewProfile)); } }
        private bool _isImportProfileRunning;
        public bool IsImportProfileRunning { get => _isImportProfileRunning; set { _isImportProfileRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(SurgeImportProfile)); } }
        private bool _isCopyRunning;
        public bool IsCopyRunning { get => _isCopyRunning; set { _isCopyRunning = value; OnPropertyChanged(); } }
        private bool _isDeleteRunning;
        public bool IsDeleteRunning { get => _isDeleteRunning; set { _isDeleteRunning = value; OnPropertyChanged(); } }

        public bool SurgeNewProfile => IsNewProfileRunning || (Profiles?.Count ?? 0) == 0;
        public bool SurgeImportProfile => IsImportProfileRunning || (Profiles?.Count ?? 0) == 0;

        private ProfileEntry? _selectedProfile;
        public ProfileEntry? SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); RecomputeStats(); OnPropertyChanged(nameof(SelectedProfileDifferentDrive)); }
        }

        public bool SelectedProfileDifferentDrive
        {
            get
            {
                if (SelectedProfile == null) return false;
                return !IsSameVolume(SelectedProfile.RootPath, Options?.GameFolder);
            }
        }

        private int _statModCount;
        public int StatModCount { get => _statModCount; private set { _statModCount = value; OnPropertyChanged(); } }
        private int _statPatchGroupCount;
        public int StatPatchGroupCount { get => _statPatchGroupCount; private set { _statPatchGroupCount = value; OnPropertyChanged(); } }
        private int _statEnabledPatchGroupCount;
        public int StatEnabledPatchGroupCount { get => _statEnabledPatchGroupCount; private set { _statEnabledPatchGroupCount = value; OnPropertyChanged(); } }

        public ICommand NewProfileCommand { get; }
        public ICommand ImportProfileCommand { get; }
        public ICommand CopyProfileCommand { get; }
        public ICommand CloseProfileTabCommand { get; }
        public ICommand SetGameFolderCommand { get; }
        public ICommand SaveOptionsCommand { get; }
        public ICommand OpenSelectedProfileCommand { get; }
        public ICommand RenameProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ChangeFolderCommand { get; }
        public ICommand EnableProfileCommand { get; }
        public ICommand DisableProfileCommand { get; }
        public ICommand LaunchGameCommand { get; }

        public ILogService Log => _log;

        public ManagedMainViewModel(Action<ProfileEntry>? openProfileTab = null)
        {
            _optionStore = new OptionStore();
            _log = new LogService();
            _openProfileTab = openProfileTab;
            Options = _optionStore.LoadOrCreate();
            Profiles = new ObservableCollection<ProfileEntry>(Options.Profiles);
            Profiles.CollectionChanged += (_, __) => { OnPropertyChanged(nameof(SurgeNewProfile)); OnPropertyChanged(nameof(SurgeImportProfile)); };

            NewProfileCommand = new RelayCommand(_ => NewProfile());
            ImportProfileCommand = new RelayCommand(_ => ImportProfile());
            CopyProfileCommand = new RelayCommand(p => CopyProfile(p as ProfileEntry));
            CloseProfileTabCommand = new RelayCommand(p => CloseProfileTab(p as ProfileEntry));
            SetGameFolderCommand = new RelayCommand(_ => SetGameFolder());
            SaveOptionsCommand = new RelayCommand(_ => Save());
            OpenSelectedProfileCommand = new RelayCommand(p => OpenProfile(p as ProfileEntry));
            RenameProfileCommand = new RelayCommand(p => RenameProfile(p as ProfileEntry));
            DeleteProfileCommand = new RelayCommand(p => DeleteProfile(p as ProfileEntry));
            OpenFolderCommand = new RelayCommand(p => OpenFolder(p as ProfileEntry));
            ChangeFolderCommand = new RelayCommand(p => ChangeFolder(p as ProfileEntry));
            EnableProfileCommand = new RelayCommand(p => EnableProfile(p as ProfileEntry));
            DisableProfileCommand = new RelayCommand(p => DisableProfile(p as ProfileEntry));
            LaunchGameCommand = new RelayCommand(_ =>
            {
                _log.Log(ManagedMain.Resources.Strings.SR_Log_LaunchGame);
                GameLauncher.LaunchHelldivers2(_log);
            });
        }

        private static bool IsSameVolume(string? p1, string? p2)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(p1) || string.IsNullOrWhiteSpace(p2)) return true;
                var r1 = Path.GetPathRoot(Path.GetFullPath(p1));
                var r2 = Path.GetPathRoot(Path.GetFullPath(p2));
                return !string.IsNullOrWhiteSpace(r1) && !string.IsNullOrWhiteSpace(r2) && string.Equals(r1, r2, System.StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        private void HintIfDifferentDrive(string? profileRoot)
        {
            try
            {
                if (!IsSameVolume(profileRoot, Options?.GameFolder))
                {
                    _log.Log(ManagedMain.Resources.Strings.SR_Log_DifferentDriveHint);
                }
            }
            catch { }
        }

        public void EnsureModsLoaded(ProfileEntry p)
        {
            if (p.Mods.Count > 0) return;
            var mods = _modListStore.Load(p.RootPath);
            p.Mods.Clear();
            foreach (var m in mods) p.Mods.Add(m);
            if (SelectedProfile == p) RecomputeStats();
        }

        // Unified enable strategy: async + NormalizeAndRelinkAll, following ProfileModsView behavior
        private async void EnableProfile(ProfileEntry? p)
        {
            if (p == null) return;
            if (string.IsNullOrWhiteSpace(Options.GameFolder)) { _log.Log(ManagedMain.Resources.Strings.SR_Log_GameFolderNotConfigured); return; }
            EnsureModsLoaded(p);

            // Disable others first (persist immediately)
            foreach (var other in Profiles.Where(x => !ReferenceEquals(x, p))) other.IsEnabled = false;
            Save();

            IsBusy = true; _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_EnableProfile_Starting, p.Name));
            try
            {
                // Clear and rebuild using normalized ordering
                await Task.Run(() =>
                {
                    _activation.RemoveAllPatchFiles(Options.GameFolder);
                    _ = _activation.NormalizeAndRelinkAll(p.RootPath, Options.GameFolder, p.Mods);
                });

                // Save normalized PatchN/Files
                new ModListStore().Save(p.RootPath, p.Mods);
                _log.Log(ManagedMain.Resources.Strings.SR_Log_EnableProfile_Saved);

                p.IsEnabled = true;
                Save();
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_EnableProfile_Done, p.Name));
            }
            catch (System.Exception ex)
            {
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_EnableProfile_Failed, ex.Message));
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void DisableProfile(ProfileEntry? p)
        {
            if (p == null) return;
            if (string.IsNullOrWhiteSpace(Options.GameFolder)) { _log.Log(ManagedMain.Resources.Strings.SR_Log_GameFolderNotConfigured); return; }
            EnsureModsLoaded(p);

            IsBusy = true; _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DisableProfile_Starting, p.Name));
            try
            {
                var removed = await Task.Run(() => _activation.RemoveAllPatchFiles(Options.GameFolder));
                // Save normalized anyways
                new ModListStore().Save(p.RootPath, p.Mods);
                _log.Log(ManagedMain.Resources.Strings.SR_Log_DisableProfile_Saved);
                p.IsEnabled = false;
                Save();
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DisableProfile_Done, removed));
            }
            catch (System.Exception ex)
            {
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DisableProfile_Failed, ex.Message));
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RecomputeStats()
        {
            if (SelectedProfile == null)
            {
                StatModCount = 0; StatPatchGroupCount = 0; StatEnabledPatchGroupCount = 0; return;
            }
            var (mods, groups, enabledGroups) = _statsService.ComputeStats(SelectedProfile.RootPath);
            StatModCount = mods; StatPatchGroupCount = groups; StatEnabledPatchGroupCount = enabledGroups;
        }

        private void OpenProfile(ProfileEntry? p)
        {
            if (p == null) return;
            p.IsOpen = true;
            _openProfileTab?.Invoke(p);
            SyncAndSave();
            _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_OpenTab, p.Name));
        }

        private void NewProfile()
        {
            try
            {
                IsBusy = true; IsNewProfileRunning = true;
                var baseDir = AppContext.BaseDirectory;
                var name = "Profile_" + (Profiles.Count + 1);
                var root = Path.Combine(baseDir, "workspaces", name);
                Directory.CreateDirectory(root);
                var profile = new ProfileEntry { Name = name, RootPath = root, IsOpen = false, IsEnabled = false };
                Profiles.Add(profile);
                SyncAndSave();
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_NewProfile, name));
                _log.Log(ManagedMain.Resources.Strings.SR_Log_Tip_DoubleClickToEdit);
                HintIfDifferentDrive(root);
                OnPropertyChanged(nameof(SelectedProfileDifferentDrive));
            }
            catch (System.Exception ex)
            {
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_NewProfile_Failed, ex.Message));
            }
            finally { IsNewProfileRunning = false; IsBusy = false; }
        }

        private void ImportProfile()
        {
            IsBusy = true; IsImportProfileRunning = true;
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    CheckFileExists = false,
                    CheckPathExists = true,
                    FileName = "Select Folder then click Open",
                    Filter = "Folders|*.none",
                    Title = ManagedMain.Resources.Strings.SR_Dlg_ImportProfile_Title
                };
                if (dlg.ShowDialog() == true)
                {
                    var folder = Path.GetDirectoryName(dlg.FileName)!;
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        var name = new DirectoryInfo(folder).Name;
                        var profile = new ProfileEntry { Name = name, RootPath = folder, IsOpen = false, IsEnabled = false };
                        Profiles.Add(profile);
                        SyncAndSave();
                        _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_ImportProfile, name));
                        _log.Log(ManagedMain.Resources.Strings.SR_Log_Tip_DoubleClickToEdit);
                        HintIfDifferentDrive(folder);
                        OnPropertyChanged(nameof(SelectedProfileDifferentDrive));
                    }
                }
            }
            catch (System.Exception ex)
            {
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_ImportProfile_Failed, ex.Message));
            }
            finally { IsImportProfileRunning = false; IsBusy = false; }
        }

        private async void CopyProfile(ProfileEntry? src)
        {
            if (src == null) return;
            var baseDir = Path.GetDirectoryName(src.RootPath);
            if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;
            var name = src.Name + "_Copy";
            var target = Path.Combine(baseDir!, name);
            try
            {
                IsBusy = true; IsCopyRunning = true; _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_CopyProfile_Starting, src.Name, name));
                await Task.Run(() => { CopyDirectory(src.RootPath, target); });
                var profile = new ProfileEntry { Name = name, RootPath = target, IsOpen = false, IsEnabled = false };
                Profiles.Add(profile);
                SyncAndSave();
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_CopyProfile_Done, src.Name, name));
                HintIfDifferentDrive(target);
                OnPropertyChanged(nameof(SelectedProfileDifferentDrive));
            }
            catch (System.Exception ex)
            {
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_CopyProfile_Failed, ex.Message));
            }
            finally { IsCopyRunning = false; IsBusy = false; }
        }

        private async void DeleteProfile(ProfileEntry? p)
        {
            if (p == null) return;
            try
            {
                var modsLoaded = _modListStore.Load(p.RootPath);
                int modCount = modsLoaded?.Count() ?? 0;
                if (modCount > 0)
                {
                    var result = System.Windows.MessageBox.Show(
                        string.Format(ManagedMain.Resources.Strings.SR_Confirm_DeleteProfile_Message, modCount),
                        ManagedMain.Resources.Strings.SR_Confirm_DeleteProfile_Title,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) { _log.Log(ManagedMain.Resources.Strings.SR_Btn_Cancel); return; }
                }

                IsBusy = true; IsDeleteRunning = true; _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DeleteProfile_Starting, p.Name));
                await Task.Run(() => { if (Directory.Exists(p.RootPath)) Directory.Delete(p.RootPath, true); });
                Profiles.Remove(p);
                SyncAndSave();
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DeleteProfile_Done, p.Name));
            }
            catch (System.Exception ex)
            {
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DeleteProfile_Failed, ex.Message));
            }
            finally { IsDeleteRunning = false; IsBusy = false; }
        }

        private void CloseProfileTab(ProfileEntry? p)
        {
            if (p == null) return;
            p.IsOpen = false;
            SyncAndSave();
            _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_CloseTab, p.Name));
        }

        private void SetGameFolder()
        {
            var current = Options.GameFolder?.Trim();
            bool firstClick = string.IsNullOrWhiteSpace(current) || !Directory.Exists(current);
            if (firstClick)
            {
                try
                {
                    var found = SteamLocator.TryFindHelldivers2Data();
                    if (!string.IsNullOrWhiteSpace(found) && Directory.Exists(found))
                    {
                        Options.GameFolder = found;
                        Save();
                        _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_AutoGameFolderSet2, found));
                        OnPropertyChanged(nameof(SelectedProfileDifferentDrive));
                        return;
                    }
                    else
                    {
                        _log.Log(ManagedMain.Resources.Strings.SR_Log_AutoGameFolderNotFound2);
                    }
                }
                catch { _log.Log(ManagedMain.Resources.Strings.SR_Log_AutoGameFolderFailed2); }
            }

            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = ManagedMain.Resources.Strings.SR_Dlg_SelectGameData_Desc,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            var result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                Options.GameFolder = dlg.SelectedPath;
                Save();
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_GameFolderSet2, Options.GameFolder));
                OnPropertyChanged(nameof(SelectedProfileDifferentDrive));
            }
        }

        private void RenameProfile(ProfileEntry? p)
        {
            if (p == null) return;
            var current = p.Name;
            var newName = PromptNewName(current);
            if (string.IsNullOrWhiteSpace(newName) || newName == current) return;
            if (!IsValidName(newName)) { _log.Log(ManagedMain.Resources.Strings.SR_Log_InvalidName); return; }
            try
            {
                var parent = Path.GetDirectoryName(p.RootPath) ?? AppContext.BaseDirectory;
                var target = Path.Combine(parent, newName);
                if (string.Equals(p.RootPath, target, System.StringComparison.OrdinalIgnoreCase))
                {
                    p.Name = newName; SyncAndSave(); return;
                }
                if (Directory.Exists(target)) { _log.Log(ManagedMain.Resources.Strings.SR_Log_DirExists); return; }
                if (Directory.Exists(p.RootPath)) Directory.Move(p.RootPath, target);
                p.RootPath = target;
                p.Name = newName;
                SyncAndSave();
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Rename_Done, current, newName));
                OnPropertyChanged(nameof(SelectedProfileDifferentDrive));
            }
            catch (System.Exception ex)
            {
                _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Rename_Failed, ex.Message));
            }
        }

        private static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static string PromptNewName(string current)
        {
            try
            {
                var dlg = new ManagedMain.Views.RenameDialog(current)
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                return dlg.ShowDialog() == true ? dlg.NewName.Trim() : current;
            }
            catch { return current; }
        }

        private void OpenFolder(ProfileEntry? p)
        {
            if (p == null) return;
            try { Process.Start("explorer.exe", p.RootPath); } catch (System.Exception ex) { _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_OpenPathFailed, ex.Message)); }
        }

        private void ChangeFolder(ProfileEntry? p)
        {
            if (p == null) return;
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = ManagedMain.Resources.Strings.SR_Dlg_SelectNewProfileRoot_Desc,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            var result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var oldRoot = p.RootPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
                var newRoot = (dlg.SelectedPath ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(newRoot)) { _log.Log(ManagedMain.Resources.Strings.SR_Log_PathInvalid); return; }
                if (string.Equals(oldRoot, newRoot, System.StringComparison.OrdinalIgnoreCase)) { _log.Log(ManagedMain.Resources.Strings.SR_Log_PathUnchanged); return; }

                if (!string.IsNullOrEmpty(oldRoot))
                {
                    if (newRoot.StartsWith(oldRoot + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase))
                    { System.Windows.MessageBox.Show(ManagedMain.Resources.Strings.SR_Dlg_PathSubOfOld, ManagedMain.Resources.Strings.SR_Dlg_PathChange_CannotTitle, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    if (oldRoot.StartsWith(newRoot + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase))
                    { System.Windows.MessageBox.Show(ManagedMain.Resources.Strings.SR_Dlg_PathParentOfOld, ManagedMain.Resources.Strings.SR_Dlg_PathChange_CannotTitle, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                }

                try
                {
                    Directory.CreateDirectory(newRoot);
                    // New structure only: mods live directly under profile root
                    int moved = MoveDirectoryContents(oldRoot, newRoot);
                    try { if (!string.IsNullOrEmpty(oldRoot) && Directory.Exists(oldRoot) && !Directory.EnumerateFileSystemEntries(oldRoot).Any()) Directory.Delete(oldRoot, true); } catch { }

                    p.RootPath = newRoot;
                    SyncAndSave();
                    _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_PathUpdated, moved));
                    HintIfDifferentDrive(newRoot);
                    OnPropertyChanged(nameof(SelectedProfileDifferentDrive));
                }
                catch (System.Exception ex)
                {
                    _log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_MoveFailed, ex.Message));
                }
            }
        }

        private static int MoveDirectoryContents(string source, string dest)
        {
            if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) return 0;
            Directory.CreateDirectory(dest);
            int count = 0;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                var target = Path.Combine(dest, Path.GetFileName(file));
                try
                {
                    if (File.Exists(target)) { try { File.Delete(target); } catch { } }
                    File.Move(file, target);
                    count++;
                }
                catch
                {
                    try { File.Copy(file, target, true); File.Delete(file); count++; } catch { }
                }
            }
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                var targetDir = Path.Combine(dest, name);
                try
                {
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.Move(dir, targetDir);
                        count++;
                    }
                    else
                    {
                        count += MergeDirectory(dir, targetDir);
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
                catch
                {
                    count += MergeDirectory(dir, targetDir);
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            return count;
        }

        private static int MergeDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            int count = 0;
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var target = Path.Combine(destDir, Path.GetFileName(file));
                try
                {
                    if (File.Exists(target)) { try { File.Delete(target); } catch { } }
                    File.Move(file, target); count++;
                }
                catch
                {
                    try { File.Copy(file, target, true); File.Delete(file); count++; } catch { }
                }
            }
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var childDest = Path.Combine(destDir, Path.GetFileName(dir));
                count += MergeDirectory(dir, childDest);
            }
            return count;
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSub = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSub);
            }
        }

        private void SyncAndSave()
        {
            Options.Profiles.Clear();
            foreach (var p in Profiles) Options.Profiles.Add(p);
            _optionStore.Save(Options);
        }

        public void Save()
        {
            SyncAndSave();
            OnPropertyChanged(nameof(SelectedProfileDifferentDrive));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
