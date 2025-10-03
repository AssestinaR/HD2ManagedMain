using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace HD2ModManager.Services
{
    public enum NotificationLevel { Info, Warning, Error }

    public class NotificationItem
    {
        public string Message { get; set; } = string.Empty;
        public NotificationLevel Level { get; set; } = NotificationLevel.Info;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan? Duration { get; set; } = TimeSpan.FromSeconds(6);
    }

    public class NotificationService
    {
        private readonly ObservableCollection<NotificationItem> _items = new();
        public ReadOnlyObservableCollection<NotificationItem> Items { get; }

        public NotificationService()
        {
            Items = new ReadOnlyObservableCollection<NotificationItem>(_items);
        }

        public void Show(string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null)
        {
            var item = new NotificationItem { Message = message, Level = level, Duration = duration ?? TimeSpan.FromSeconds(6) };
            _items.Add(item);
            if (item.Duration.HasValue)
            {
                _ = AutoDismissAsync(item);
            }
        }

        private async Task AutoDismissAsync(NotificationItem item)
        {
            try
            {
                await Task.Delay(item.Duration!.Value);
                _items.Remove(item);
            }
            catch { }
        }

        public void Clear() => _items.Clear();
    }
}
