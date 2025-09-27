using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ManagedMain.Views
{
    public partial class RemarkDialog : Window, INotifyPropertyChanged
    {
        private string _text = string.Empty;
        public string Text { get => _text; set { _text = value; OnPropertyChanged(); } }

        public RemarkDialog(string current)
        {
            InitializeComponent(); // use the auto-generated InitializeComponent from XAML build (BAML)
            DataContext = this;
            Text = current ?? string.Empty;
            Loaded += (s,e) =>
            {
                if (FindName("RemarkBox") is System.Windows.Controls.TextBox tb)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name=null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
