using System.IO;
using System.Windows;
using HD2ModCore.Domain;
using Microsoft.Win32;

namespace HD2ModManager.Views;

// Purpose: Confirms a non-destructive same-key reconstruction destination or automatic library import.
public partial class SameKeyReconstructionOutputWindow : Window
{
	public SameKeyReconstructionOutputWindow()
	{
		InitializeComponent();
		SummaryText.Text = "将根据当前 Game Data 直接生成独立验证候选。生成过程会执行必要的结构性检查；请随后使用 Blender 或游戏完成外部验证。";
	}

	public string OutputDirectory { get; private set; } = string.Empty;
	public bool ImportToLibrary => ImportOption.IsChecked == true;

	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new OpenFolderDialog { Title = "选择正式验证候选输出文件夹", Multiselect = false };
		if (dialog.ShowDialog(this) == true) OutputPathText.Text = dialog.FolderName;
	}

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		var root = ImportToLibrary
			? Path.Combine(Path.GetTempPath(), "HD2ModManager", "SameKeyReconstruction", Guid.NewGuid().ToString("N"))
			: OutputPathText.Text.Trim();
		if (string.IsNullOrWhiteSpace(root))
		{
			MessageBox.Show(this, "请选择输出文件夹。", "生成当前版本正式验证候选", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
		{
			MessageBox.Show(this, "输出根文件夹必须为空。", "生成当前版本正式验证候选", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		OutputDirectory = root;
		DialogResult = true;
	}
}
