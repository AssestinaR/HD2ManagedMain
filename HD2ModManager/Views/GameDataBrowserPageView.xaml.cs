using System.Windows.Controls;

namespace HD2ModManager.Views
{
    // 作用：保留 Game Data archive 表选择交互的页面承载视图。
    public partial class GameDataBrowserPageView : UserControl
    {
        public GameDataBrowserPageView() => InitializeComponent();
        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { }
    }
}