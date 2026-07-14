using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace HD2ModManager.Views;

// Purpose: Collects a non-destructive material packaging output or automatic library import choice.
public partial class MaterialPackagingOutputWindow : Window
{
	public MaterialPackagingOutputWindow() => InitializeComponent();

	public string OutputDirectory { get; private set; } = string.Empty;
	public bool ImportToLibrary => ImportOption.IsChecked == true;

	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new OpenFolderDialog { Title = "选择材质打包输出文件夹", Multiselect = false };
		if (dialog.ShowDialog(this) == true) OutputPathText.Text = dialog.FolderName;
	}

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		var root = ImportToLibrary
			? Path.Combine(Path.GetTempPath(), "HD2ModManager", "MaterialPackaging", Guid.NewGuid().ToString("N"))
			: OutputPathText.Text.Trim();
		if (string.IsNullOrWhiteSpace(root))
		{
			MessageBox.Show(this, "请选择输出文件夹。", "材质打包输出", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
		{
			MessageBox.Show(this, "输出文件夹必须为空。", "材质打包输出", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		OutputDirectory = root;
		DialogResult = true;
	}
}
