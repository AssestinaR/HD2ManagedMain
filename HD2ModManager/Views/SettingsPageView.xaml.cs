using System.Windows.Controls;
using System.Windows;
using HD2ModManager.Services;

namespace HD2ModManager.Views
{
    public partial class SettingsPageView : UserControl
    {
        public string ModLibraryFolder { get => SettingsService.GetModLibraryFolder(); set => SettingsService.SetModLibraryFolder(value); }
        public string GameDataFolder { get => SettingsService.GetGameDataFolder(); set => SettingsService.SetGameDataFolder(value); }
        public SettingsPageView()
        {
            InitializeComponent();
            Loaded += SettingsPageView_Loaded;
        }

        private void SettingsPageView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // If language not set, prompt user to select
            var current = SettingsService.GetLanguage();
            if (string.IsNullOrWhiteSpace(current))
            {
                var result = MessageBox.Show(
                    "选择语言?\nYes: 中文(zh-CN) / No: English(en-US)",
                    "Language",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                var culture = result == MessageBoxResult.Yes ? "zh-CN" : "en-US";
                if (SettingsService.SetLanguage(culture))
                {
                    MessageBox.Show("语言已设置为 " + culture + "，请重启应用以生效。", "Language", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            // init toggles
            try { AutoCleanupToggle.IsChecked = SettingsService.GetAutoCleanup(); } catch { }
            try { AutoOpenTagEditToggle.IsChecked = SettingsService.GetAutoOpenTagEdit(); } catch { }
            try { EnableLibraryImagesToggle.IsChecked = SettingsService.GetEnableLibraryImages(); } catch { }
        }

        private void BrowseModFolder_Click(object sender, RoutedEventArgs e)
        {
            var current = SettingsService.GetModLibraryFolder();
            var input = Microsoft.VisualBasic.Interaction.InputBox("输入或粘贴文件夹路径:", "Browse", current);
            if (!string.IsNullOrWhiteSpace(input))
            {
                SettingsService.SetModLibraryFolder(input);
                if (this.FindName("ModFolderBox") is TextBox tb) tb.Text = input;
            }
        }

        private void OpenModFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var p = SettingsService.GetModLibraryFolder();
                if (!System.IO.Directory.Exists(p)) System.IO.Directory.CreateDirectory(p);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = p, UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, "Open Folder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetModFolder_Click(object sender, RoutedEventArgs e)
        {
            var def = SettingsService.GetDefaultModLibraryFolder();
            SettingsService.SetModLibraryFolder(def);
            if (this.FindName("ModFolderBox") is TextBox tb) tb.Text = def;
        }

        private void BrowseGameDataFolder_Click(object sender, RoutedEventArgs e)
        {
            var current = SettingsService.GetGameDataFolder();
            var input = Microsoft.VisualBasic.Interaction.InputBox("输入或粘贴游戏数据目录路径:", "Browse", current);
            if (!string.IsNullOrWhiteSpace(input))
            {
                SettingsService.SetGameDataFolder(input);
                if (this.FindName("GameDataFolderBox") is TextBox tb) tb.Text = input;
            }
        }

        private void OpenGameDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var p = SettingsService.GetGameDataFolder();
                if (string.IsNullOrWhiteSpace(p))
                {
                    p = SettingsService.TryDetectAndSetGameDataFolder();
                }
                if (!string.IsNullOrWhiteSpace(p))
                {
                    if (!System.IO.Directory.Exists(p)) System.IO.Directory.CreateDirectory(p);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = p, UseShellExecute = true });
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, "Open Folder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetGameDataFolder_Click(object sender, RoutedEventArgs e)
        {
            var detected = SettingsService.TryDetectAndSetGameDataFolder();
            if (!string.IsNullOrWhiteSpace(detected) && this.FindName("GameDataFolderBox") is TextBox tb)
            {
                tb.Text = detected;
            }
        }

        private void AutoCleanupToggle_Click(object sender, RoutedEventArgs e)
        {
            try { SettingsService.SetAutoCleanup(AutoCleanupToggle.IsChecked == true); } catch { }
        }
        private void AutoOpenTagEditToggle_Click(object sender, RoutedEventArgs e)
        {
            try { SettingsService.SetAutoOpenTagEdit(AutoOpenTagEditToggle.IsChecked == true); } catch { }
        }
        private void EnableLibraryImagesToggle_Click(object sender, RoutedEventArgs e)
        {
            try { SettingsService.SetEnableLibraryImages(EnableLibraryImagesToggle.IsChecked == true); } catch { }
        }
        

        private void ReloadTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
                var configDir = System.IO.Path.Combine(baseDir, "config");
                var catalog = HD2ModManager.Services.TagCatalogService.Instance;
                catalog.RebuildFromCsv(baseDir);
                catalog.Save();
                catalog.Load(configDir);
                MessageBox.Show("Tags reloaded from CSV.", "Tags", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to reload tags: {ex.Message}", "Tags", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
