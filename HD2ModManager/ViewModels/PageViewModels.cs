using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HD2ModManager.ViewModels
{
    public abstract class PageViewModel : BaseViewModel
    {
        private string _title = string.Empty;
        public string Title { get => _title; set => SetField(ref _title, value); }
        public ObservableCollection<CardViewModel> Cards { get; } = new();
    }

    public abstract class CardViewModel : BaseViewModel
    {
        private string _title = string.Empty;
        public string Title { get => _title; set => SetField(ref _title, value); }
        private string? _description;
        public string? Description { get => _description; set => SetField(ref _description, value); }
        private string? _icon;
        public string? Icon { get => _icon; set => SetField(ref _icon, value); }
    }

    public class NavCardViewModel : CardViewModel
    {
        private string _targetPageKey = string.Empty;
        public string TargetPageKey { get => _targetPageKey; set => SetField(ref _targetPageKey, value); }
        public RelayCommand NavigateCommand { get; }
        public NavCardViewModel(System.Action<string> navigator)
        {
            NavigateCommand = new RelayCommand(() => navigator(TargetPageKey));
        }
    }

    public class ActionCardViewModel : CardViewModel
    {
        public RelayCommand ActionCommand { get; }
        public ActionCardViewModel(System.Action action)
        {
            ActionCommand = new RelayCommand(action);
        }
    }

    public class ProfileCardViewModel : CardViewModel
    {
        private bool _isActive;
        public bool IsActive { get => _isActive; set => SetField(ref _isActive, value); }
        public RelayCommand EnableCommand { get; set; } = null!;
        public RelayCommand DisableCommand { get; set; } = null!;
        public RelayCommand RenameCommand { get; set; } = null!;
        public RelayCommand DeleteCommand { get; set; } = null!;
    }

    public class SettingToggleCardViewModel : CardViewModel
    {
        private bool _value;
        public bool Value { get => _value; set => SetField(ref _value, value); }
    }

    public class StatusCardViewModel : CardViewModel
    {
        private readonly Func<string> _getText;
        public string Text { get => _text; private set => SetField(ref _text, value); }
        private string _text = string.Empty;
        public StatusCardViewModel(Func<string> getter)
        {
            _getText = getter;
            Refresh();
        }
        public void Refresh() { Text = _getText(); }
    }

    public class HomePageViewModel : PageViewModel
    {
        private readonly Services.ProfileService _profiles;
        private readonly Services.ModLibraryService _library;
        private readonly Services.ImportQueueService _queue;
        public HomePageViewModel(System.Action<string> navigator, Services.ProfileService profiles, Services.ModLibraryService library, Services.ImportQueueService queue)
        {
            Title = HD2ModManager.Resources.Strings.Breadcrumb_Home;
            _profiles = profiles;
            _library = library;
            _queue = queue;
            Cards.Add(new ActionCardViewModel(CreateNewProfile) { Title = HD2ModManager.Resources.Strings.Home_NewProfile, Description = HD2ModManager.Resources.Strings.Home_NewProfile });
            Cards.Add(new NavCardViewModel(navigator) { Title = HD2ModManager.Resources.Strings.Home_Settings, Description = HD2ModManager.Resources.Strings.Home_Settings, TargetPageKey = "settings" });
            // 预留模组库入口，待库页面落地后启用
            LoadExistingProfiles();

            // 临时测试卡片：添加一个模组到库并保存
            Cards.Add(new NavCardViewModel(navigator) { Title = HD2ModManager.Resources.Strings.Home_ModLibrary, Description = "Open library", TargetPageKey = "library" });
        }

        private void CreateNewProfile()
        {
            var key = _profiles.CreateNew();
            Cards.Add(CreateProfileCard(key));
        }

        private void LoadExistingProfiles()
        {
            var dict = _profiles.All();
            foreach (var key in dict.Keys)
            {
                Cards.Add(CreateProfileCard(key));
            }
        }

        private void AddDummyMod()
        {
            var mod = new HD2ModManager.Models.ModEntity
            {
                Name = "Dummy Mod",
                Description = "Test entry",
                FileGroups = new List<HD2ModManager.Models.FileGroup>
                {
                    new HD2ModManager.Models.FileGroup { HexPrefix = "AAAAAAAAAAAAAAAA", PatchN = 1, RelativePath = "", Files = new List<string>{ "AAAAAAAAAAAAAAAA.patch_1" } }
                }
            };
            _library.Add(mod);
        }

        private void SaveLibrary()
        {
            _library.Save();
            // 触发状态卡刷新
            foreach (var s in Cards.OfType<StatusCardViewModel>()) s.Refresh();
        }

        public void RefreshLibraryStatus()
        {
            foreach (var s in Cards.OfType<StatusCardViewModel>()) s.Refresh();
        }

        private ProfileCardViewModel CreateProfileCard(string key)
        {
            var vm = new ProfileCardViewModel { Title = key, Description = null, IsActive = _profiles.ActiveKey == key };
            vm.EnableCommand = new RelayCommand(() =>
            {
                var currentKey = vm.Title;
                _profiles.SetActive(currentKey);
                foreach (var c in Cards.OfType<ProfileCardViewModel>()) c.IsActive = c.Title == currentKey;
            });
            vm.DisableCommand = new RelayCommand(() =>
            {
                var currentKey = vm.Title;
                if (_profiles.ActiveKey == currentKey) { _profiles.DisableActive(); vm.IsActive = false; }
            });
            vm.RenameCommand = new RelayCommand(() =>
            {
                var oldKey = vm.Title;
                var newName = PromptForName(oldKey);
                if (!string.IsNullOrWhiteSpace(newName) && newName != oldKey && _profiles.Rename(oldKey, newName))
                {
                    vm.Title = newName;
                }
                else
                {
                    System.Windows.MessageBox.Show("重命名失败：名称无效或与现有配置冲突。", "Rename", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            });
            vm.DeleteCommand = new RelayCommand(() =>
            {
                var currentKey = vm.Title;
                // 删除前确认
                var res = System.Windows.MessageBox.Show($"删除配置 '{currentKey}'?", "Confirm", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (res == System.Windows.MessageBoxResult.Yes)
                {
                    if (_profiles.Remove(currentKey))
                    {
                        var target = Cards.FirstOrDefault(c => c is ProfileCardViewModel p && p.Title == currentKey);
                        if (target != null) Cards.Remove(target);
                        // 更新激活标记显示
                        var active = _profiles.ActiveKey;
                        foreach (var c in Cards.OfType<ProfileCardViewModel>()) c.IsActive = c.Title == active;
                    }
                }
            });
            return vm;
        }

        private static string PromptForName(string old)
        {
            try
            {
                // 使用简单输入框（依赖 Microsoft.VisualBasic）
                return Microsoft.VisualBasic.Interaction.InputBox("输入新名称（仅文件名，不含扩展名）:", "Rename", old);
            }
            catch
            {
                return old;
            }
        }
    }

    public class SettingsPageViewModel : PageViewModel
    {
        public SettingsPageViewModel()
        {
            ApplyLocalization();
            // 将详细设置项留待后续实现（语言、导入策略、链接策略等）
        }

        private void ApplyLocalization()
        {
            Title = HD2ModManager.Resources.Strings.Home_Settings;
        }

        // Language card texts come from satellite resources; no hardcoded methods needed
    }
}
