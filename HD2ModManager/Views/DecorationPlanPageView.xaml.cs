using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

public partial class DecorationPlanPageView : UserControl
{
    public DecorationPlanPageView() => InitializeComponent();

    private void OnTargetModRowClick(object sender, ModListRowEventArgs e)
    {
        if (e.Item is DecorationTargetModItem item)
            item.IsSelected = !item.IsSelected;
    }
}
