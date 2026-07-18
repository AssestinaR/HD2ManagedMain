using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace HD2ModManager.Services
{
    public enum NotificationLevel { Info, Warning, Error }

    public class NotificationItem
    {
        public string Message { get; set; } = string.Empty;
        public NotificationLevel Level { get; set; } = NotificationLevel.Info;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan? Duration { get; set; } = TimeSpan.FromSeconds(6);
        public bool IsUnread { get; set; } = true;
    }

    public class NotificationService
    {
        private readonly ObservableCollection<NotificationItem> _items = new();
        private readonly ObservableCollection<NotificationItem> _history = new();
        public ReadOnlyObservableCollection<NotificationItem> Items { get; }
        public ReadOnlyObservableCollection<NotificationItem> History { get; }
        public event EventHandler? Changed;

        public NotificationService()
        {
            Items = new ReadOnlyObservableCollection<NotificationItem>(_items);
            History = new ReadOnlyObservableCollection<NotificationItem>(_history);
        }

        public void Show(string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null)
        {
            var item = new NotificationItem { Message = message, Level = level, Duration = duration ?? TimeSpan.FromSeconds(6) };
            RunOnUi(() =>
            {
                _items.Add(item);
                _history.Insert(0, item);
                if (item.Duration.HasValue) _ = AutoDismissAsync(item);
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }

        public void MarkAllRead()
        {
            foreach (var item in _history) item.IsUnread = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private async Task AutoDismissAsync(NotificationItem item)
        {
            try
            {
                await Task.Delay(item.Duration!.Value);
                RunOnUi(() => _items.Remove(item));
            }
            catch { }
        }

        public void Clear() => RunOnUi(_items.Clear);

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _ = dispatcher.InvokeAsync(action);
        }
    }
}
