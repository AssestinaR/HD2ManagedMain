using System.IO;
using System.Windows;
using System.Windows.Controls;
using HD2ModCore.Domain;
using Microsoft.Win32;

namespace HD2ModManager.Views;

// Purpose: Selects an empty output folder for an isolated experimental cross-armor candidate.
public partial class CrossArmorTransferCandidateOutputWindow : Window
{
	public CrossArmorTransferCandidateOutputWindow() => InitializeComponent();
	public string OutputDirectory { get; private set; } = string.Empty;
	public CrossArmorMaterialBindingMode MaterialBindingMode { get; private set; } = CrossArmorMaterialBindingMode.PreserveSourceReferences;

	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new OpenFolderDialog { Title = "选择跨护甲验证候选输出文件夹", Multiselect = false };
		if (dialog.ShowDialog(this) == true) OutputPathText.Text = dialog.FolderName;
	}

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		var root = OutputPathText.Text.Trim();
		if (string.IsNullOrWhiteSpace(root)) { MessageBox.Show(this, "请选择输出文件夹。", "生成跨护甲验证候选", MessageBoxButton.OK, MessageBoxImage.Information); return; }
		if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) { MessageBox.Show(this, "输出根文件夹必须为空。", "生成跨护甲验证候选", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
		if (MaterialBindingModeBox.SelectedItem is not ComboBoxItem { Tag: string mode } || !Enum.TryParse(mode, out CrossArmorMaterialBindingMode parsedMode))
		{
			MessageBox.Show(this, "请选择有效的材质绑定模式。", "生成跨护甲验证候选", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		OutputDirectory = root;
		MaterialBindingMode = parsedMode;
		DialogResult = true;
	}
}