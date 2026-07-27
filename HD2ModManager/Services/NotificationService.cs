using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace HD2ModManager.Services
{
    public enum NotificationLevel { Info, Warning, Error }

    public class NotificationItem : INotifyPropertyChanged
    {
        private string _message = string.Empty;
        private NotificationLevel _level = NotificationLevel.Info;
        public string Message { get => _message; set => SetField(ref _message, value); }
        public NotificationLevel Level { get => _level; set => SetField(ref _level, value); }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan? Duration { get; set; } = TimeSpan.FromSeconds(6);
        public bool IsUnread { get; set; } = true;
        internal string? Tag { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
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
                _history.Add(item);
                if (item.Duration.HasValue) _ = AutoDismissAsync(item);
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }

        public void ShowOrUpdate(string key, string message, NotificationLevel level = NotificationLevel.Info, TimeSpan? duration = null)
        {
            RunOnUi(() =>
            {
                var item = _items.FirstOrDefault(candidate => string.Equals(candidate.Tag, key, StringComparison.Ordinal));
                if (item is null)
                {
                    item = new NotificationItem { Message = message, Level = level, Duration = duration, Tag = key };
                    _items.Add(item);
                    _history.Add(item);
                    Changed?.Invoke(this, EventArgs.Empty);
                    return;
                }

                item.Message = message;
                item.Level = level;
                item.IsUnread = true;
                item.Duration = duration;
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
