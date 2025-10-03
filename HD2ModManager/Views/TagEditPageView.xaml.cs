using System.Linq;
using System.Windows.Controls;
using System.Windows;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    public partial class TagEditPageView : UserControl
    {
        public TagEditPageView()
        {
            InitializeComponent();
        }

        private TagEditPageViewModel? VM => DataContext as TagEditPageViewModel;

        private void OnAvailableTagClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (VM == null) return;
            var lb = sender as ListBox;
            if (lb?.SelectedItem is string s)
            {
                if (!VM.Tags.Contains(s)) VM.Tags.Add(s);
                if (VM.AvailableTags.Contains(s)) VM.AvailableTags.Remove(s);
            }
        }

        private void OnSelectedTagClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (VM == null) return;
            var lb = sender as ListBox;
            if (lb?.SelectedItem is string s)
            {
                if (!VM.AvailableTags.Contains(s)) VM.AvailableTags.Add(s);
                if (VM.Tags.Contains(s)) VM.Tags.Remove(s);
            }
        }

        private void OnAddCustomTag(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var q = VM.Query?.Trim();
            if (string.IsNullOrWhiteSpace(q)) return;
            var catalog = HD2ModManager.Services.TagCatalogService.Instance;
            var added = catalog.AddCustomTag(q);
            if (added)
            {
                catalog.Save();
            }
            if (!VM.Tags.Contains(q)) VM.Tags.Add(q);
            if (VM.AvailableTags.Contains(q)) VM.AvailableTags.Remove(q);
        }
    }
}
