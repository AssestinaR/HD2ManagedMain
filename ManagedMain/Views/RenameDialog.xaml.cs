using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ManagedMain.Views
{
    public partial class RenameDialog : Window, INotifyPropertyChanged
    {
        private string _newName = string.Empty;
        public string NewName { get => _newName; set { _newName = value; OnPropertyChanged(); } }

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            DataContext = this;
            NewName = currentName;
            Loaded += (s,e) => NameBox.Focus();
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
