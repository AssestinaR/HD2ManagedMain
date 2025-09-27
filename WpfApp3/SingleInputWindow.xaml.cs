using System.Windows;
using System.Windows.Input;

namespace LiberTeaManager
{
    public partial class SingleInputWindow : Window
    {
        public string ResultText { get; private set; } = string.Empty;
        private bool _saved = false;
        public SingleInputWindow(string title, string prompt, string initial)
        {
            InitializeComponent();
            Title = title;
            LblPrompt.Text = prompt;
            InputBox.Text = initial ?? string.Empty;
            InputBox.Focus();
            InputBox.SelectAll();
        }

        private void SaveAndClose()
        {
            if (_saved) return;
            _saved = true;
            ResultText = InputBox.Text.Trim();
            DialogResult = true; // 标记为保存
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 关闭即保存
            if (!_saved)
            {
                ResultText = InputBox.Text.Trim();
                DialogResult = true;
            }
            base.OnClosing(e);
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveAndClose();
                e.Handled = true;
            }
        }
    }
}