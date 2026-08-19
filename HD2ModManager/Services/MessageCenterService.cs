using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace HD2ModManager.Services
{
    // Purpose: Provides stable message-center sections. Active work is deliberately
    // separate from ordinary notifications so status cannot be displaced by chatter.
    public sealed class MessageCenterItem : INotifyPropertyChanged, IDisposable
    {
        private readonly NotificationItem? _notification;
        private readonly BackgroundTaskItem? _task;

        public MessageCenterItem(NotificationItem notification)
        {
            _notification = notification;
            _notification.PropertyChanged += OnSourcePropertyChanged;
        }

        public MessageCenterItem(BackgroundTaskItem task)
        {
            _task = task;
            _task.PropertyChanged += OnSourcePropertyChanged;
        }

        public DateTime OccurredAt => _notification?.UpdatedAt ?? _task?.FinishedAt ?? _task?.StartedAt ?? _task?.CreatedAt ?? DateTime.UtcNow;
        public string Title => _task?.Name ?? _notification?.Message ?? string.Empty;
        public string Detail => _task is null
            ? BuildNotificationDetail(_notification!)
            : string.IsNullOrWhiteSpace(_task.Stage) ? _task.StatusText : $"{_task.StatusText} · {_task.Stage}";
        public NotificationLevel Level => _notification?.Level ?? _task?.Status switch
        {
            BackgroundTaskStatus.Failed => NotificationLevel.Error,
            _ => NotificationLevel.Info,
        };
        public bool IsTask => _task is not null;
        public bool IsUnread => _notification?.IsUnread == true;
        public bool CanCancel => _task?.CanCancel == true;
        public bool CanRetry => _task?.CanRetry == true;
        public bool HasProgress => _task?.HasProgress == true;
        public double Progress => _task?.Progress ?? 0d;
        public bool HasReport => _task?.HasReport == true;
        public bool HasOutputDirectory => _task?.HasOutputDirectory == true;
        public bool IsAcknowledged => _notification?.IsAcknowledged == true || _task?.IsAcknowledged == true;
        public bool CanAcknowledge => _notification?.IsAcknowledged == false
            || _task is { IsActive: true, IsFocusDismissed: false }
            || _task is { Status: BackgroundTaskStatus.Failed, IsAcknowledged: false };
        public BackgroundTaskItem? Task => _task;
        internal NotificationItem? Notification => _notification;
        public string CopyText => _task is null
            ? string.IsNullOrWhiteSpace(Detail) ? Title : $"{Title}{Environment.NewLine}{Detail}"
            : string.Join(Environment.NewLine, new[]
            {
                $"标题：{Title}", $"来源：{_task.Origin}", $"状态：{_task.StatusText}", $"阶段：{_task.Stage}",
                $"创建时间：{_task.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}", $"开始时间：{_task.StartedAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
                $"完成时间：{_task.FinishedAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss}", $"详情：{_task.Detail}", $"错误：{_task.Error}",
                $"输出目录：{_task.OutputDirectory}", $"报告：{_task.ReportPath}",
            });

        private static string BuildNotificationDetail(NotificationItem notification)
        {
            var source = string.IsNullOrWhiteSpace(notification.Source) ? string.Empty : $"{notification.Source} · ";
            var count = notification.OccurrenceCount > 1 ? $" · 重复 {notification.OccurrenceCount} 次" : string.Empty;
            return $"{source}{notification.UpdatedAt.ToLocalTime():HH:mm:ss}{count}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Dispose()
        {
            if (_notification is not null) _notification.PropertyChanged -= OnSourcePropertyChanged;
            if (_task is not null) _task.PropertyChanged -= OnSourcePropertyChanged;
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public sealed class MessageCenterService
    {
        private readonly NotificationService _notifications;
        private readonly BackgroundTaskService _tasks;
        private readonly ObservableCollection<MessageCenterItem> _activeTasks = new();
        private readonly ObservableCollection<MessageCenterItem> _attentionItems = new();
        private readonly ObservableCollection<MessageCenterItem> _recentNotifications = new();
        private readonly ObservableCollection<MessageCenterItem> _popupItems = new();
        private readonly Dispatcher? _dispatcher;
        private int _refreshQueued;

        public ReadOnlyObservableCollection<MessageCenterItem> ActiveTasks { get; }
        public ReadOnlyObservableCollection<MessageCenterItem> AttentionItems { get; }
        public ReadOnlyObservableCollection<MessageCenterItem> RecentNotifications { get; }
        public ReadOnlyObservableCollection<MessageCenterItem> PopupItems { get; }
        public int RunningTaskCount => _activeTasks.Count;
        public int AttentionCount => _attentionItems.Count(item => item.IsUnread || item.IsTask);
        public int RecentUnreadCount => _recentNotifications.Count(item => item.IsUnread);
        public event EventHandler? Changed;

        public MessageCenterService(NotificationService notifications, BackgroundTaskService tasks)
        {
            _notifications = notifications;
            _tasks = tasks;
            _dispatcher = Application.Current?.Dispatcher;
            ActiveTasks = new ReadOnlyObservableCollection<MessageCenterItem>(_activeTasks);
            AttentionItems = new ReadOnlyObservableCollection<MessageCenterItem>(_attentionItems);
            RecentNotifications = new ReadOnlyObservableCollection<MessageCenterItem>(_recentNotifications);
            PopupItems = new ReadOnlyObservableCollection<MessageCenterItem>(_popupItems);
            _notifications.Changed += (_, _) => Refresh();
            _tasks.Changed += (_, args) => { if (args.RequiresProjectionRefresh) Refresh(); };
            Refresh();
        }

        public void Refresh()
        {
            if (_dispatcher is null || _dispatcher.CheckAccess())
            {
                RefreshCore();
                return;
            }

            if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
                _ = _dispatcher.InvokeAsync(() => { Interlocked.Exchange(ref _refreshQueued, 0); RefreshCore(); }, DispatcherPriority.DataBind);
        }

        private void RefreshCore()
        {
            Replace(_activeTasks, _tasks.Tasks
                .Where(task => task.IsActive && !task.IsInformationCenter)
                .OrderByDescending(task => task.StartedAt ?? task.CreatedAt)
                .Select(task => new MessageCenterItem(task)));

            Replace(_attentionItems, _tasks.Tasks
                .Where(task => task.Status == BackgroundTaskStatus.Failed && !task.IsAcknowledged && !task.IsInformationCenter)
                .Select(task => new MessageCenterItem(task))
                .Concat(_notifications.History
                    .Where(item => item.Level is NotificationLevel.Warning or NotificationLevel.Error && !item.IsAcknowledged)
                    .Select(item => new MessageCenterItem(item)))
                .OrderByDescending(item => item.OccurredAt)
                .Take(40));

            Replace(_recentNotifications, _tasks.Tasks
                .Where(task => !task.IsInformationCenter && (task.Status is BackgroundTaskStatus.Completed or BackgroundTaskStatus.Canceled || task.Status == BackgroundTaskStatus.Failed && task.IsAcknowledged))
                .Select(task => new MessageCenterItem(task))
                .Concat(_notifications.History
                    .Where(item => item.Level == NotificationLevel.Info || item.IsAcknowledged)
                    .Select(item => new MessageCenterItem(item)))
                .OrderByDescending(item => item.OccurredAt)
                .Take(80)
                );

            // The popup is intentionally independent from the history preview. Important
            // messages stay visible together until acknowledgement; transient info follows
            // NotificationService.Items and therefore keeps its own expiry.
            Replace(_popupItems, _tasks.Tasks
                .Where(task => task.Status == BackgroundTaskStatus.Failed && !task.IsAcknowledged && !task.IsInformationCenter)
                .Select(task => new MessageCenterItem(task))
                .Concat(_notifications.History
                    .Where(item => item.Level is NotificationLevel.Warning or NotificationLevel.Error && !item.IsAcknowledged)
                    .Select(item => new MessageCenterItem(item)))
                .OrderByDescending(item => item.OccurredAt)
                .Concat(_tasks.Tasks
                    .Where(task => task.IsActive && !task.IsFocusDismissed && !task.IsInformationCenter)
                    .OrderByDescending(task => task.StartedAt ?? task.CreatedAt)
                    .Select(task => new MessageCenterItem(task)))
                .Concat(_notifications.Items
                    .Where(item => item.Level == NotificationLevel.Info && !item.IsAcknowledged)
                    .OrderByDescending(item => item.UpdatedAt)
                    .Select(item => new MessageCenterItem(item)))
                .GroupBy(item => item.Task?.Id ?? item.Notification?.Id ?? string.Empty)
                .Select(group => group.First()));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void MarkAttentionViewed()
        {
            _notifications.MarkAttentionViewed();
            foreach (var task in _tasks.Tasks.Where(task => task.Status == BackgroundTaskStatus.Failed && !task.IsAcknowledged && !task.IsInformationCenter))
            {
                task.RecordAttentionView();
            }
        }

        public void Acknowledge(MessageCenterItem item)
        {
            if (item is null || !item.CanAcknowledge) return;
            if (item.Notification is { } notification) _notifications.Acknowledge(notification);
            else if (item.Task is { IsActive: true } task) task.DismissFromFocus();
            else item.Task?.Acknowledge();
        }

        private static void Replace(ObservableCollection<MessageCenterItem> destination, IEnumerable<MessageCenterItem> source)
        {
            foreach (var item in destination) item.Dispose();
            destination.Clear();
            foreach (var item in source) destination.Add(item);
        }
    }
}
