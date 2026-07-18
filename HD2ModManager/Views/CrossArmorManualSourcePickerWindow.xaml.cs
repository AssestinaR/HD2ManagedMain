using System.Windows;
using HD2ModCore.Domain;

namespace HD2ModManager.Views;

// Purpose: Lets an advanced user lock one physical target mesh to an explicitly chosen same-part source mesh.
public partial class CrossArmorManualSourcePickerWindow : Window
{
	public EquipmentUnitPart? SelectedSource => Candidates.SelectedItem as CrossArmorManualSourceChoice is { } choice ? choice.Part : null;

	public CrossArmorManualSourcePickerWindow(CrossArmorTransferMapping target, IReadOnlyList<EquipmentUnitPart> candidates)
	{
		InitializeComponent();
		PromptText.Text = $"为 {target.Target.PartKind} / {target.Target.Layer} 的物理目标选择强制来源。锁定后，其余未锁定对象仍会自动顺延分配。";
		Candidates.ItemsSource = candidates.Select(part => new CrossArmorManualSourceChoice(part)).ToArray();
		Candidates.SelectedIndex = 0;
	}

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		if (SelectedSource is null) return;
		DialogResult = true;
	}
}

// Purpose: Formats one selectable source mesh in the manual mapping picker.
public sealed class CrossArmorManualSourceChoice
{
	public EquipmentUnitPart Part { get; }
	public string Display => $"{Part.Layer} / {Part.BodyVariant} — mesh {Part.MeshInfoIndex} — {Part.SemanticName} — Unit 0x{Part.UnitAssetKey.FileId:x16}";
	public CrossArmorManualSourceChoice(EquipmentUnitPart part) => Part = part;
}
