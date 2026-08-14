using System.Windows;
using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    public partial class HomePageView : UserControl
    {
        public HomePageView()
        {
            InitializeComponent();
        }

        private void OnOpenModListPanelTestClick(object sender, RoutedEventArgs e)
        {
            if (Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
                shell.OpenModListPanelTest();
        }
    }
}
