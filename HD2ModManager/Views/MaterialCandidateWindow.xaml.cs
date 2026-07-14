using System.Windows;
using HD2ModCore.Domain;

namespace HD2ModManager.Views;

// Purpose: Lets the user explicitly select one fully compatible flat library material Mod.
public partial class MaterialCandidateWindow : Window
{
	public MaterialCandidateWindow() => InitializeComponent();

	public MaterialCandidateItem? SelectedCandidate { get; private set; }

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		if (CandidateList.SelectedItem is not MaterialCandidateItem selected)
		{
			MessageBox.Show(this, "请选择一个材质包。", "选择材质包", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		if (!selected.IsCompatible)
		{
			MessageBox.Show(this, selected.BlockerSummary, "材质包不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}
		SelectedCandidate = selected;
		DialogResult = true;
	}
}

public sealed record MaterialCandidateItem(MaterialPackageCandidate Candidate)
{
	public ModNodeId NodeId => Candidate.NodeId;
	public string Name => Candidate.Name;
	public bool IsCompatible => Candidate.IsCompatible;
	public int MatchingMaterialCount => Candidate.MatchingMaterialCount;
	public int MissingMaterialCount => Candidate.MissingMaterialCount;
	public int MissingTextureCount => Candidate.MissingTextureCount;
	public string BlockerSummary => Candidate.Blockers.Count == 0 ? string.Empty : string.Join("；", Candidate.Blockers);
	public string StatusText => IsCompatible ? (Candidate.Blockers.Count == 0 ? "完整适配" : "可写出（提醒）") : "不匹配";
	public string StatusBrush => IsCompatible ? (Candidate.Blockers.Count == 0 ? "#237A3B" : "#B06A00") : "#A33";
 }
