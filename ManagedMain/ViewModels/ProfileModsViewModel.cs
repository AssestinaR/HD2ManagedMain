using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ManagedMain.Models;
using ManagedMain.Services;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ManagedMain.ViewModels
{
    public class ProfileModsViewModel : INotifyPropertyChanged
    {
        private readonly ModListStore _store = new();
        private readonly ImportService _import = new();
        private readonly ExportService _export = new();
        private readonly ActivationService _activate = new();
        public ProfileEntry Profile { get; }
        public ObservableCollection<MainModItem> Mods { get; }
        public ILogService Log { get; } = new LogService();

        private string _gameFolder = string.Empty;
        public string GameFolder { get => _gameFolder; set { _gameFolder = value; OnPropertyChanged(); } }

        public ICommand SaveCommand { get; }
        public ICommand ImportFolderCommand { get; }
        public ICommand ImportArchiveCommand { get; }
        public ICommand NewEmptyModCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand ExportSelectedCommand { get; }
        public ICommand EnableSelectedCommand { get; }
        public ICommand DisableSelectedCommand { get; }
        public ICommand RenameSelectedCommand { get; }
        public ICommand AddOptionCommand { get; }
        public ICommand AddSubOptionCommand { get; }
        public ICommand ChangeImageCommand { get; }
        public ICommand OpenFolderSelectedCommand { get; }
        public ICommand EditRemarkCommand { get; }
        public ICommand OpenUrlCommand { get; }
        public ICommand EditUrlCommand { get; }
        public ICommand ToggleEnableCommand { get; }

        private object? _selectedItem;
        public object? SelectedItem
        {
            get => _selectedItem;
            set { _selectedItem = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        private bool _isImportFolderRunning;
        public bool IsImportFolderRunning { get => _isImportFolderRunning; set { _isImportFolderRunning = value; OnPropertyChanged(); } }

        private bool _isImportArchiveRunning;
        public bool IsImportArchiveRunning { get => _isImportArchiveRunning; set { _isImportArchiveRunning = value; OnPropertyChanged(); } }

        public ProfileModsViewModel(ProfileEntry profile)
        {
            Profile = profile;
            Mods = _store.Load(Profile.RootPath);
            // Initialize GameFolder from persisted options to avoid OptionStore fallback timing issues
            try { GameFolder = new OptionStore().LoadOrCreate().GameFolder; } catch { GameFolder = string.Empty; }

            SaveCommand = new RelayCommand(_ => Save());
            ImportFolderCommand = new RelayCommand(_ => ImportFolder());
            ImportArchiveCommand = new RelayCommand(_ => ImportArchive());
            NewEmptyModCommand = new RelayCommand(_ => NewEmptyMod());
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected());
            ExportSelectedCommand = new RelayCommand(_ => ExportSelected());
            EnableSelectedCommand = new RelayCommand(_ => EnableSelected());
            DisableSelectedCommand = new RelayCommand(_ => DisableSelected());
            RenameSelectedCommand = new RelayCommand(_ => RenameSelected());
            AddOptionCommand = new RelayCommand(_ => AddOption());
            AddSubOptionCommand = new RelayCommand(_ => AddSubOption());
            ChangeImageCommand = new RelayCommand(_ => ChangeImage());
            OpenFolderSelectedCommand = new RelayCommand(_ => OpenFolderSelected());
            EditRemarkCommand = new RelayCommand(_ => EditRemark());
            OpenUrlCommand = new RelayCommand(p =>
            {
                var url = (p as string)?.Trim();
                if (string.IsNullOrWhiteSpace(url)) return;
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            });
            EditUrlCommand = new RelayCommand(_ => EditUrl());
            ToggleEnableCommand = new RelayCommand(_ =>
            {
                var item = SelectedItem;
                if (item == null) return;
                int state = item switch
                {
                    MainModItem m => m.Enabled,
                    OptionItem o => o.Enabled,
                    SubOptionItem s => s.Enabled,
                    _ => 0
                };
                if (state == 0) EnableSelectedCommand.Execute(null); else DisableSelectedCommand.Execute(null);
            });
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Public helper to recompute enabled states after structure changes
        public void RecalculateAllEnabledStates()
        {
            foreach (var m in Mods)
            {
                UpdateEnabled(m);
            }
        }

        // Edit URL
        private void EditUrl()
        {
            var target = SelectedItem; if (target == null) return;
            string current = target switch
            {
                MainModItem m => m.Url ?? string.Empty,
                OptionItem o => o.Url ?? string.Empty,
                SubOptionItem s => s.Url ?? string.Empty,
                _ => string.Empty
            };
            try
            {
                var dlg = new ManagedMain.Views.InputDialog
                {
                    Owner = System.Windows.Application.Current?.MainWindow,
                    Title = ManagedMain.Resources.Strings.SR_Title_EditLink,
                    Message = ManagedMain.Resources.Strings.SR_Prompt_EnterUrl,
                    Text = current
                };
                if (dlg.ShowDialog() == true)
                {
                    var text = dlg.Text?.Trim() ?? string.Empty;
                    switch (target)
                    {
                        case MainModItem m: m.Url = text; break;
                        case OptionItem o: o.Url = text; break;
                        case SubOptionItem s: s.Url = text; break;
                    }
                    Save(); Log.Log(ManagedMain.Resources.Strings.SR_Log_LinkUpdated);
                }
            }
            catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_EditLinkFailed, ex.Message)); }
        }

        // Collect all selected items (Main, Option, Sub) in flat list
        private List<object> GetAllSelected()
        {
            var list = new List<object>();
            foreach (var m in Mods)
            {
                if (m.IsSelected) list.Add(m);
                foreach (var o in m.Options)
                {
                    if (o.IsSelected) list.Add(o);
                    foreach (var s in o.SubOptions) if (s.IsSelected) list.Add(s);
                }
            }
            return list;
        }

        private static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static string ReplacePathPrefix(string path, string oldPrefix, string newPrefix)
        {
            var norm = path.Replace('\\', '/');
            oldPrefix = oldPrefix.Replace('\\', '/');
            newPrefix = newPrefix.Replace('\\', '/');
            if (norm.StartsWith(oldPrefix + "/"))
                return newPrefix + norm.Substring(oldPrefix.Length);
            if (norm == oldPrefix) return newPrefix;
            return path;
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

        // Rename
        private void RenameSelected()
        {
            switch (SelectedItem)
            {
                case MainModItem main:
                {
                    var oldName = main.Name;
                    var newName = PromptNewName(oldName);
                    if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
                    if (!IsValidName(newName)) { Log.Log(ManagedMain.Resources.Strings.SR_Log_InvalidName); return; }
                    var oldDir = Path.Combine(Profile.RootPath, oldName);
                    var newDir = Path.Combine(Profile.RootPath, newName);
                    try
                    {
                        if (Directory.Exists(newDir)) { Log.Log(ManagedMain.Resources.Strings.SR_Log_DirExists); return; }
                        if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);
                        main.Name = newName; Save(); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Rename_Done, oldName, newName));
                    }
                    catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Rename_Failed, ex.Message)); }
                    break;
                }
                case OptionItem opt:
                {
                    var pm = FindParentMain(opt); if (pm == null) return;
                    var oldName = opt.Name;
                    var newName = PromptNewName(oldName);
                    if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
                    if (!IsValidName(newName)) { Log.Log(ManagedMain.Resources.Strings.SR_Log_InvalidName); return; }
                    var oldDir = Path.Combine(Profile.RootPath, pm.Name, oldName);
                    var newDir = Path.Combine(Profile.RootPath, pm.Name, newName);
                    try
                    {
                        if (Directory.Exists(newDir)) { Log.Log(ManagedMain.Resources.Strings.SR_Log_DirExists); return; }
                        if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);
                        UpdateOptionPathsAfterRename(pm, opt, oldName, newName);
                        opt.Name = newName; Save(); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Rename_Done, oldName, newName));
                    }
                    catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Rename_Failed, ex.Message)); }
                    break;
                }
                case SubOptionItem sub:
                {
                    var (pm, opt) = FindParentMainAndOption(sub); if (pm == null || opt == null) return;
                    var oldName = sub.Name;
                    var newName = PromptNewName(oldName);
                    if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
                    if (!IsValidName(newName)) { Log.Log(ManagedMain.Resources.Strings.SR_Log_InvalidName); return; }
                    var oldDir = Path.Combine(Profile.RootPath, pm.Name, opt.Name, oldName);
                    var newDir = Path.Combine(Profile.RootPath, pm.Name, opt.Name, newName);
                    try
                    {
                        if (Directory.Exists(newDir)) { Log.Log(ManagedMain.Resources.Strings.SR_Log_DirExists); return; }
                        if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);
                        UpdateSubPathsAfterRename(opt, sub, opt.Name, oldName, newName);
                        sub.Name = newName; Save(); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Rename_Done, oldName, newName));
                    }
                    catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Rename_Failed, ex.Message)); }
                    break;
                }
            }
        }

        private static void UpdateOptionPathsAfterRename(MainModItem main, OptionItem opt, string oldOpt, string newOpt)
        {
            if (!string.IsNullOrEmpty(opt.Image)) opt.Image = ReplacePathPrefix(opt.Image!, oldOpt, newOpt);
            if (!string.IsNullOrEmpty(opt.IconPath)) opt.IconPath = ReplacePathPrefix(opt.IconPath!, oldOpt, newOpt);
            foreach (var g in opt.FileGroups)
            {
                if (!string.IsNullOrEmpty(g.RelativePath)) g.RelativePath = ReplacePathPrefix(g.RelativePath!, oldOpt, newOpt);
                for (int i = 0; i < g.Files.Count; i++) g.Files[i] = ReplacePathPrefix(g.Files[i], oldOpt, newOpt);
            }
            foreach (var s in opt.SubOptions)
            {
                if (!string.IsNullOrEmpty(s.Image)) s.Image = ReplacePathPrefix(s.Image!, oldOpt, newOpt);
                if (!string.IsNullOrEmpty(s.IconPath)) s.IconPath = ReplacePathPrefix(s.IconPath!, oldOpt, newOpt);
                foreach (var g in s.FileGroups)
                {
                    if (!string.IsNullOrEmpty(g.RelativePath)) g.RelativePath = ReplacePathPrefix(g.RelativePath!, oldOpt, newOpt);
                    for (int i = 0; i < g.Files.Count; i++) g.Files[i] = ReplacePathPrefix(g.Files[i], oldOpt, newOpt);
                }
            }
        }

        private static void UpdateSubPathsAfterRename(OptionItem opt, SubOptionItem sub, string optName, string oldSub, string newSub)
        {
            string oldPrefix = optName + "/" + oldSub;
            string newPrefix = optName + "/" + newSub;
            if (!string.IsNullOrEmpty(sub.Image)) sub.Image = ReplacePathPrefix(sub.Image!, oldPrefix, newPrefix);
            if (!string.IsNullOrEmpty(sub.IconPath)) sub.IconPath = ReplacePathPrefix(sub.IconPath!, oldPrefix, newPrefix);
            foreach (var g in sub.FileGroups)
            {
                if (!string.IsNullOrEmpty(g.RelativePath)) g.RelativePath = ReplacePathPrefix(g.RelativePath!, oldPrefix, newPrefix);
                for (int i = 0; i < g.Files.Count; i++) g.Files[i] = ReplacePathPrefix(g.Files[i], oldPrefix, newPrefix);
            }
        }

        // Add option/sub
        private void AddOption()
        {
            var main = SelectedItem as MainModItem; if (main == null) return;
            var name = "Option_" + (main.Options.Count + 1);
            var opt = new OptionItem { Name = name };
            main.Options.Add(opt);
            try { Directory.CreateDirectory(Path.Combine(Profile.RootPath, main.Name, name)); }
            catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_CreateOptionFolderFailed, ex.Message)); }
            // Recompute enabled states as structure changed
            UpdateEnabled(main);
            Save(); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_AddedOption, name));
        }

        private void AddSubOption()
        {
            if (SelectedItem is not OptionItem opt) return;
            var (m, _) = FindParentMainAndOptionOf(opt); if (m == null) return;
            var name = "Sub_" + (opt.SubOptions.Count + 1);
            var sub = new SubOptionItem { Name = name };
            opt.SubOptions.Add(sub);
            try { Directory.CreateDirectory(Path.Combine(Profile.RootPath, m.Name, opt.Name, name)); }
            catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_CreateSubOptionFolderFailed, ex.Message)); }
            // Recompute enabled states as structure changed
            UpdateEnabled(m);
            Save(); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_AddedSubOption, name));
        }

        // Change image
        private void ChangeImage()
        {
            switch (SelectedItem)
            {
                case MainModItem main:
                    ChangeImageForMain(main); break;
                case OptionItem opt:
                    var pm = FindParentMain(opt); if (pm != null) ChangeImageForOption(pm, opt); break;
                case SubOptionItem sub:
                    var (m, o) = FindParentMainAndOption(sub); if (m != null && o != null) ChangeImageForSub(m, o, sub); break;
            }
        }

        private void ChangeImageForMain(MainModItem main)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = ManagedMain.Resources.Strings.SR_Btn_Image, Filter = ManagedMain.Resources.Strings.SR_Dlg_Filter_Images, CheckFileExists = true, Multiselect = false };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var modRoot = Path.Combine(Profile.RootPath, main.Name);
                    Directory.CreateDirectory(modRoot);
                    var dest = Path.Combine(modRoot, "icon.png");
                    using (var src = File.OpenRead(dlg.FileName)) using (var target = File.Create(dest)) { src.CopyTo(target); }
                    main.IconPath = "icon.png"; main.Image = main.IconPath; Save(); Log.Log(ManagedMain.Resources.Strings.SR_Log_ImageUpdated);
                }
                catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_SetImageFailed, ex.Message)); }
            }
        }
        private void ChangeImageForOption(MainModItem main, OptionItem opt)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = ManagedMain.Resources.Strings.SR_Btn_Image, Filter = ManagedMain.Resources.Strings.SR_Dlg_Filter_Images, CheckFileExists = true, Multiselect = false };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var dir = Path.Combine(Profile.RootPath, main.Name, opt.Name); Directory.CreateDirectory(dir);
                    var dest = Path.Combine(dir, "icon.png");
                    using var src = File.OpenRead(dlg.FileName); using var target = File.Create(dest); src.CopyTo(target);
                    opt.Image = opt.IconPath = opt.Name + "/" + "icon.png"; Save(); Log.Log(ManagedMain.Resources.Strings.SR_Log_ImageUpdated);
                }
                catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_SetImageFailed, ex.Message)); }
            }
        }
        private void ChangeImageForSub(MainModItem main, OptionItem opt, SubOptionItem sub)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = ManagedMain.Resources.Strings.SR_Btn_Image, Filter = ManagedMain.Resources.Strings.SR_Dlg_Filter_Images, CheckFileExists = true, Multiselect = false };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var dir = Path.Combine(Profile.RootPath, main.Name, opt.Name, sub.Name); Directory.CreateDirectory(dir);
                    var dest = Path.Combine(dir, "icon.png");
                    using var src = File.OpenRead(dlg.FileName); using var target = File.Create(dest); src.CopyTo(target);
                    sub.Image = sub.IconPath = opt.Name + "/" + sub.Name + "/" + "icon.png"; Save(); Log.Log(ManagedMain.Resources.Strings.SR_Log_ImageUpdated);
                }
                catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_SetImageFailed, ex.Message)); }
            }
        }

        // Open folder
        private void OpenFolderSelected()
        {
            try
            {
                switch (SelectedItem)
                {
                    case MainModItem main:
                        Process.Start(new ProcessStartInfo("explorer.exe", Path.Combine(Profile.RootPath, main.Name)) { UseShellExecute = true }); break;
                    case OptionItem opt:
                        var pm = FindParentMain(opt); if (pm != null) Process.Start(new ProcessStartInfo("explorer.exe", Path.Combine(Profile.RootPath, pm.Name, opt.Name)) { UseShellExecute = true }); break;
                    case SubOptionItem sub:
                        var (m, o) = FindParentMainAndOption(sub); if (m != null && o != null) Process.Start(new ProcessStartInfo("explorer.exe", Path.Combine(Profile.RootPath, m.Name, o.Name, sub.Name)) { UseShellExecute = true }); break;
                }
            }
            catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_OpenPathFailed, ex.Message)); }
        }

        // Import/Export/Delete/New
        private async void ImportFolder()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = ManagedMain.Resources.Strings.SR_Dlg_SelectExtractedModFolder_Desc, UseDescriptionForTitle = true, ShowNewFolderButton = false };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                IsBusy = true; IsImportFolderRunning = true; Log.Log(ManagedMain.Resources.Strings.SR_Log_ImportingFolder);
                try
                {
                    var item = await Task.Run(() => _import.ImportFolderAsMod(Profile.RootPath, dlg.SelectedPath));
                    Mods.Add(item); Save(); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Imported, item.Name));
                }
                catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_ImportFailed, ex.Message)); }
                finally { IsImportFolderRunning = false; IsBusy = false; }
            }
        }
        private async void ImportArchive()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = ManagedMain.Resources.Strings.SR_Dlg_SelectArchive_Title, Filter = ManagedMain.Resources.Strings.SR_Dlg_Filter_Archives, CheckFileExists = true, Multiselect = false };
            if (dlg.ShowDialog() == true)
            {
                IsBusy = true; IsImportArchiveRunning = true; Log.Log(ManagedMain.Resources.Strings.SR_Log_ImportingArchive);
                try
                {
                    var item = await Task.Run(() => _import.ImportArchiveAsMod(Profile.RootPath, dlg.FileName));
                    Mods.Add(item); Save(); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Imported, item.Name));
                }
                catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_ImportFailed, ex.Message)); }
                finally { IsImportArchiveRunning = false; IsBusy = false; }
            }
        }
        private void NewEmptyMod()
        {
            var name = "NewMod_" + (Mods.Count + 1);
            var targetRoot = Path.Combine(Profile.RootPath, name);
            try
            {
                Directory.CreateDirectory(targetRoot);
                var item = new MainModItem { Name = name };
                Mods.Add(item); Save(); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_CreatedEmptyMod, name));
            }
            catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_CreateModFailed, ex.Message)); }
        }
        private void ExportSelected()
        {
            if (SelectedItem is not MainModItem main) { Log.Log(ManagedMain.Resources.Strings.SR_Log_SelectAMod); return; }
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = ManagedMain.Resources.Strings.SR_Label_Path, UseDescriptionForTitle = true, ShowNewFolderButton = true };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try { var zip = _export.ExportMod(Profile.RootPath, main, dlg.SelectedPath, version: 1); Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_Imported, zip)); }
                catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_ImportFailed, ex.Message)); }
            }
        }
        private void DeleteSelected()
        {
            var selected = GetAllSelected(); if (selected.Count == 0 && SelectedItem != null) selected.Add(SelectedItem); if (selected.Count == 0) return;
            var gameFolder = new OptionStore().LoadOrCreate().GameFolder;
            foreach (var node in selected) CascadeSetEnabled(node, 0);
            foreach (var m in Mods) UpdateEnabled(m);

            var subItems = selected.OfType<SubOptionItem>().ToList();
            var optItems = selected.OfType<OptionItem>().ToList();
            var mainItems = selected.OfType<MainModItem>().ToList();
            foreach (var sub in subItems)
            {
                var (m, o) = FindParentMainAndOption(sub); if (m != null && o != null)
                { TryDeleteDir(Path.Combine(Profile.RootPath, m.Name, o.Name, sub.Name)); o.SubOptions.Remove(sub); }
            }
            foreach (var opt in optItems)
            {
                var pm = FindParentMain(opt); if (pm != null)
                { TryDeleteDir(Path.Combine(Profile.RootPath, pm.Name, opt.Name)); pm.Options.Remove(opt); UpdateEnabled(pm); }
            }
            foreach (var main in mainItems)
            { TryDeleteDir(Path.Combine(Profile.RootPath, main.Name)); Mods.Remove(main); }

            Save();
            _activate.RemoveAllPatchFiles(gameFolder);
            _ = _activate.NormalizeAndRelinkAll(Profile.RootPath, gameFolder, Mods);
            Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DeletedCount, selected.Count));
        }
        private static void TryDeleteDir(string dir) { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } }

        // Enable/Disable and state calc
        private async void EnableSelected()
        {
            if (IsBusy) { Log.Log(ManagedMain.Resources.Strings.SR_Log_PleaseWait); return; }
            var gameFolder = new OptionStore().LoadOrCreate().GameFolder; if (string.IsNullOrWhiteSpace(gameFolder)) { Log.Log(ManagedMain.Resources.Strings.SR_Log_GameFolderNotConfigured); return; }
            var targets = GetAllSelected(); if (targets.Count == 0 && SelectedItem != null) targets.Add(SelectedItem); if (targets.Count == 0) { Log.Log(ManagedMain.Resources.Strings.SR_Log_NoSelection); return; }
            foreach (var t in targets) CascadeSetEnabled(t, 1);
            foreach (var m in Mods) UpdateEnabled(m); Save();
            if (!Profile.IsEnabled) { Log.Log(ManagedMain.Resources.Strings.SR_Log_ProfileDisabled_SaveOnly_AndNotApply); return; }
            IsBusy = true; Log.Log(ManagedMain.Resources.Strings.SR_Log_ApplyingEnable);
            try
            {
                await Task.Run(() => { _activate.RemoveAllPatchFiles(gameFolder); _ = _activate.NormalizeAndRelinkAll(Profile.RootPath, gameFolder, Mods); });
                // Persist normalized PatchN/Files
                Save();
                Log.Log(ManagedMain.Resources.Strings.SR_Log_Done);
            }
            catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_EnableFailed2, ex.Message)); }
            finally { IsBusy = false; }
        }
        private async void DisableSelected()
        {
            if (IsBusy) { Log.Log(ManagedMain.Resources.Strings.SR_Log_PleaseWait); return; }
            var gameFolder = new OptionStore().LoadOrCreate().GameFolder; if (string.IsNullOrWhiteSpace(gameFolder)) { Log.Log(ManagedMain.Resources.Strings.SR_Log_GameFolderNotConfigured); return; }
            var targets = GetAllSelected(); if (targets.Count == 0 && SelectedItem != null) targets.Add(SelectedItem); if (targets.Count == 0) { Log.Log(ManagedMain.Resources.Strings.SR_Log_NoSelection); return; }
            foreach (var t in targets) CascadeSetEnabled(t, 0);
            foreach (var m in Mods) UpdateEnabled(m); Save();
            if (!Profile.IsEnabled) { Log.Log(ManagedMain.Resources.Strings.SR_Log_ProfileDisabled_SaveOnly); return; }
            IsBusy = true; Log.Log(ManagedMain.Resources.Strings.SR_Log_ApplyingDisable);
            try
            {
                await Task.Run(() => { _activate.RemoveAllPatchFiles(gameFolder); _ = _activate.NormalizeAndRelinkAll(Profile.RootPath, gameFolder, Mods); });
                // Persist normalized as well
                Save();
                Log.Log(ManagedMain.Resources.Strings.SR_Log_Done);
            }
            catch (System.Exception ex) { Log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_DisableFailed2, ex.Message)); }
            finally { IsBusy = false; }
        }
        private void CascadeSetEnabled(object? node, int value)
        {
            switch (node)
            {
                case MainModItem main:
                    main.Enabled = value;
                    foreach (var o in main.Options)
                    { o.Enabled = value; foreach (var s in o.SubOptions) s.Enabled = value; }
                    break;
                case OptionItem opt:
                    opt.Enabled = value; foreach (var s in opt.SubOptions) s.Enabled = value; break;
                case SubOptionItem sub:
                    sub.Enabled = value; break;
            }
        }
        private void UpdateEnabled(MainModItem main)
        {
            int enabledOptions = 0, totalOptions = main.Options.Count;
            foreach (var o in main.Options)
            {
                int enabledSubs = 0, totalSubs = o.SubOptions.Count;
                foreach (var s in o.SubOptions) if (s.Enabled == 1) enabledSubs++;
                o.Enabled = totalSubs == 0 ? o.Enabled : (enabledSubs == 0 ? 0 : (enabledSubs == totalSubs ? 1 : 2));
                if (o.Enabled == 1) enabledOptions++; else if (o.Enabled == 2) enabledOptions += 1;
            }
            if (totalOptions == 0)
            {
                // No options: keep main.Enabled as explicitly set by user/cascade
                // do not force-enable based on FileGroups count
            }
            else
            {
                main.Enabled = enabledOptions == 0 ? 0 : (enabledOptions >= totalOptions ? 1 : 2);
            }
        }

        public void Save() => _store.Save(Profile.RootPath, Mods);

        // Parent lookups
        private (MainModItem?, OptionItem?) FindParentMainAndOption(SubOptionItem sub)
        {
            foreach (var m in Mods)
            {
                foreach (var opt in m.Options)
                {
                    if (opt.SubOptions.Contains(sub)) return (m, opt);
                }
            }
            return (null, null);
        }
        private MainModItem? FindParentMain(OptionItem opt)
        { foreach (var m in Mods) if (m.Options.Contains(opt)) return m; return null; }
        private (MainModItem?, OptionItem?) FindParentMainAndOptionOf(OptionItem opt)
        { foreach (var m in Mods) if (m.Options.Contains(opt)) return (m, opt); return (null, null); }

        private void EditRemark()
        {
            object? target = SelectedItem; if (target == null) return;
            string current = target switch
            {
                MainModItem m => m.Description ?? string.Empty,
                OptionItem o => o.Description ?? string.Empty,
                SubOptionItem s => s.Description ?? string.Empty,
                _ => string.Empty
            };
            try
            {
                var dlg = new ManagedMain.Views.RemarkDialog(current) { Owner = System.Windows.Application.Current?.MainWindow };
                if (dlg.ShowDialog() == true)
                {
                    var text = dlg.Text ?? string.Empty;
                    switch (target)
                    {
                        case MainModItem m: m.Description = text; break;
                        case OptionItem o: o.Description = text; break;
                        case SubOptionItem s: s.Description = text; break;
                    }
                    Save(); Log.Log(ManagedMain.Resources.Strings.SR_Log_RemarkUpdated);
                }
            }
            catch (System.Exception ex) { Log.Log($"{string.Format(ManagedMain.Resources.Strings.SR_Log_DisableFailed2, ex.Message)}"); }
        }
    }
}
