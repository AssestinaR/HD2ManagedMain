using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace HD2ModManager.Services
{
    // 作用：将后台任务和普通通知投影为统一、按时间递增的消息流。
    public sealed class MessageCenterItem
    {
        private readonly NotificationItem? _notification;
        private readonly BackgroundTaskItem? _task;

        public MessageCenterItem(NotificationItem notification) => _notification = notification;
        public MessageCenterItem(BackgroundTaskItem task) => _task = task;

        public DateTime CreatedAt => _notification?.CreatedAt ?? _task?.CreatedAt ?? DateTime.UtcNow;
        public bool IsTask => _task is not null;
        public string Title => _task?.Name ?? _notification?.Message ?? string.Empty;
        public string Detail => _task is null
            ? CreatedAt.ToLocalTime().ToString("HH:mm:ss")
            : string.IsNullOrWhiteSpace(_task.Stage) ? _task.StatusText : $"{_task.StatusText} · {_task.Stage}";
        public NotificationLevel Level => _notification?.Level ?? _task?.Status switch
        {
            BackgroundTaskStatus.Failed => NotificationLevel.Error,
            _ => NotificationLevel.Info
        };
        public bool CanCancel => _task?.CanCancel == true;
        public bool CanRetry => _task?.CanRetry == true;
        public BackgroundTaskItem? Task => _task;
        public string CopyText
        {
            get
            {
                if (_task is null) return string.IsNullOrWhiteSpace(Detail) ? Title : $"{Title}{Environment.NewLine}{Detail}";
                var lines = new[]
                {
                    $"标题：{Title}", $"来源：{_task.Origin}", $"状态：{_task.StatusText}", $"阶段：{_task.Stage}",
                    $"创建时间：{_task.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}", $"开始时间：{_task.StartedAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
                    $"完成时间：{_task.FinishedAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss}", $"详情：{_task.Detail}", $"错误：{_task.Error}"
                };
                return string.Join(Environment.NewLine, lines);
            }
        }
    }

    public sealed class MessageCenterService
    {
        private readonly NotificationService _notifications;
        private readonly BackgroundTaskService _tasks;
        private readonly ObservableCollection<MessageCenterItem> _items = new();
        private readonly Dispatcher? _dispatcher;
        private int _refreshQueued;

        public ReadOnlyObservableCollection<MessageCenterItem> Items { get; }
        public event EventHandler? Changed;

        public MessageCenterService(NotificationService notifications, BackgroundTaskService tasks)
        {
            _notifications = notifications;
            _tasks = tasks;
            _dispatcher = Application.Current?.Dispatcher;
            Items = new ReadOnlyObservableCollection<MessageCenterItem>(_items);
            _notifications.Changed += (_, _) => Refresh();
            _tasks.Changed += (_, _) => Refresh();
            Refresh();
        }

        public void Refresh()
        {
            if (_dispatcher is not null)
            {
                if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
                    _ = _dispatcher.InvokeAsync(() => { Interlocked.Exchange(ref _refreshQueued, 0); RefreshCore(); }, DispatcherPriority.DataBind);
                return;
            }
            RefreshCore();
        }

        private void RefreshCore()
        {
            var items = _notifications.History.Select(item => new MessageCenterItem(item))
                .Concat(_tasks.Tasks.Select(task => new MessageCenterItem(task)))
                .OrderBy(item => item.CreatedAt)
                .ToArray();
            _items.Clear();
            foreach (var item in items) _items.Add(item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
