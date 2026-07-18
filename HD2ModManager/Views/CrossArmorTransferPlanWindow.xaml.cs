using System.IO;
using System.Windows;
using System.Windows.Controls;
using HD2ModCore.Domain;
using Microsoft.Win32;

namespace HD2ModManager.Views;

// Purpose: Hosts the simplified no-write source-to-target Armor or Helmet transfer plan.
public partial class CrossArmorTransferPlanWindow : Window
{
	public CrossArmorTransferPlanWindow(CrossArmorTransferPlanWindowViewModel viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
	}

	private void ExportJson_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not CrossArmorTransferPlanWindowViewModel viewModel) return;
		var dialog = new SaveFileDialog
		{
			Title = "导出跨护甲只读计划 JSON",
			Filter = "JSON 文件 (*.json)|*.json",
			FileName = "cross-armor-transfer-plan.json",
			AddExtension = true,
			DefaultExt = ".json"
		};
		if (dialog.ShowDialog(this) != true) return;
		try
		{
			viewModel.ExportJson(dialog.FileName);
			MessageBox.Show(this, "当前界面计划已导出为 JSON。", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			MessageBox.Show(this, $"导出 JSON 失败：{exception.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private async void GenerateCandidate_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not CrossArmorTransferPlanWindowViewModel viewModel || !viewModel.CanGenerateCandidate) return;
		var plan = viewModel.GetCurrentPlan();
		if (plan is null || !plan.CanContinue) return;
		var dialog = new CrossArmorTransferCandidateOutputWindow { Owner = this };
		if (dialog.ShowDialog() != true) return;
		viewModel.CandidateGenerationRunning = true;
		try
		{
			var result = await HD2ModCore.Infrastructure.CoreServices.CreateCrossArmorTransferCandidateService()
				.GenerateCandidateAsync(new CrossArmorTransferCandidateRequest(viewModel.SourcePatchTocPath, viewModel.GameDataDirectory, dialog.OutputDirectory, plan, dialog.MaterialBindingMode));
			if (!result.IsSuccessful)
			{
				MessageBox.Show(this, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)), "生成失败", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}
			MessageBox.Show(this, $"验证候选已生成。\nUnit：{result.OutputUnitCount}\n替换 mesh：{result.ReplacementMeshCount}\n极小化 mesh：{result.MinifiedMeshCount}\n\n报告：{result.ReportPath}", "生成完成", MessageBoxButton.OK, MessageBoxImage.Information);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
		{
			MessageBox.Show(this, $"生成候选失败：{exception.Message}", "生成失败", MessageBoxButton.OK, MessageBoxImage.Error);
		}
		finally { viewModel.CandidateGenerationRunning = false; }
	}

	private void ForceSource_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not CrossArmorTransferPlanWindowViewModel viewModel || !viewModel.AllowManualMappings) return;
		if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: ListBox { SelectedItem: CrossArmorTransferMapping target } } }) return;
		var candidates = viewModel.SourceParts.Where(part => part.PartKind == target.Target.PartKind).ToArray();
		if (candidates.Length == 0)
		{
			MessageBox.Show(this, "当前来源筛选中没有同部位模型可强制指定。", "手动映射", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		var picker = new CrossArmorManualSourcePickerWindow(target, candidates) { Owner = this };
		if (picker.ShowDialog() == true && picker.SelectedSource is { } source) viewModel.SetManualMapping(target, source);
	}

	private void ClearForcedMapping_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not CrossArmorTransferPlanWindowViewModel viewModel || !viewModel.AllowManualMappings) return;
		if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: ListBox { SelectedItem: CrossArmorTransferMapping target } } }) return;
		viewModel.ClearManualMapping(target);
	}

	private void SuppressAutomaticMapping_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not CrossArmorTransferPlanWindowViewModel viewModel || !viewModel.AllowManualMappings) return;
		if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: ListBox { SelectedItem: CrossArmorTransferMapping target } } }) return;
		viewModel.SuppressAutomaticMapping(target);
	}

	private void RestoreAutomaticMapping_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not CrossArmorTransferPlanWindowViewModel viewModel || !viewModel.AllowManualMappings) return;
		if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: ListBox { SelectedItem: CrossArmorTransferMapping target } } }) return;
		viewModel.RestoreAutomaticMapping(target);
	}
}
