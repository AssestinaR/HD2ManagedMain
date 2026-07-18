using System.Windows.Controls;
using System.Windows.Input;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：允许用户从右槽 AssetKey 表复制选中的完整事实。
    public partial class GameDataArchiveDetailsPageView : UserControl
    {
        public GameDataArchiveDetailsPageView() => InitializeComponent();
        private void OnAssetDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is GameDataArchiveDetailsPageViewModel viewModel && sender is DataGrid { SelectedItem: GameDataArchiveAssetRowViewModel row })
            {
                viewModel.CopyAsset(row);
            }
        }
    }
}