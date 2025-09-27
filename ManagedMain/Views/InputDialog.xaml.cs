using System.Windows;

namespace ManagedMain.Views
{
    public partial class InputDialog : Window
    {
        public string? Text { get => InputBox.Text; set => InputBox.Text = value ?? string.Empty; }
        public string Message { get => MsgText.Text; set => MsgText.Text = value; }
        public InputDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
