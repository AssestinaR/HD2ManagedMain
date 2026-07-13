using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Specialized;
using System.Threading;
using System.Windows;

namespace HD2ModManager.Services
{
    // 作用：集中记录管理器后台任务，为状态页和任务详情窗口提供可观察数据。
    public enum BackgroundTaskStatus
    {
        Queued,
        Running,
        Completed,
        Failed,
        Canceled,
    }

    public enum BackgroundTaskKind
    {
        Import,
        RefreshLibrary,
        UpdateAssetMetadata,
        BuildAssetIndex,
        Other,
    }

    public sealed class BackgroundTaskItem : INotifyPropertyChanged
    {
        private BackgroundTaskStatus _status;
        private string _stage = "等待执行";
        private string? _error;
        private bool _isSelected;
        private readonly CancellationTokenSource _cancellation = new();

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public BackgroundTaskKind Kind { get; }
        public string Name { get; }
        public string? Detail { get; }
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; private set; }
        public BackgroundTaskStatus Status { get => _status; private set => SetField(ref _status, value); }
        public string Stage { get => _stage; private set => SetField(ref _stage, value); }
        public string? Error { get => _error; private set => SetField(ref _error, value); }
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
        public bool IsActive => Status is BackgroundTaskStatus.Queued or BackgroundTaskStatus.Running;
        public CancellationToken CancellationToken => _cancellation.Token;
        public bool CanCancel => IsActive;
        public string StatusText => Status switch
        {
            BackgroundTaskStatus.Queued => "排队中",
            BackgroundTaskStatus.Running => "进行中",
            BackgroundTaskStatus.Completed => "已完成",
            BackgroundTaskStatus.Failed => "失败",
            BackgroundTaskStatus.Canceled => "已取消",
            _ => "未知",
        };
        public string KindIcon => Kind switch
        {
            BackgroundTaskKind.Import => "⇩",
            BackgroundTaskKind.RefreshLibrary => "⟳",
            BackgroundTaskKind.UpdateAssetMetadata => "◇",
            BackgroundTaskKind.BuildAssetIndex => "⌕",
            _ => "•",
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        public BackgroundTaskItem(BackgroundTaskKind kind, string name, string? detail = null)
        {
            Kind = kind;
            Name = name;
            Detail = detail;
            _status = BackgroundTaskStatus.Queued;
        }

        public void MarkRunning(string stage)
        {
            Status = BackgroundTaskStatus.Running;
            UpdateStage(stage);
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanCancel));
        }

        public void UpdateStage(string stage)
        {
            Stage = string.IsNullOrWhiteSpace(stage) ? "处理中" : stage;
        }

        public void MarkCompleted()
        {
            Status = BackgroundTaskStatus.Completed;
            FinishedAt = DateTime.UtcNow;
            UpdateStage("已完成");
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanCancel));
        }

        public void MarkFailed(string error)
        {
            Status = BackgroundTaskStatus.Failed;
            Error = error;
            FinishedAt = DateTime.UtcNow;
            UpdateStage("处理失败");
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanCancel));
        }

        public void MarkCanceled()
        {
            Status = BackgroundTaskStatus.Canceled;
            FinishedAt = DateTime.UtcNow;
            UpdateStage("已取消");
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanCancel));
        }

        public void Cancel()
        {
            if (!IsActive) return;
            _cancellation.Cancel();
            if (Status == BackgroundTaskStatus.Queued)
            {
                MarkCanceled();
            }
            else
            {
                UpdateStage("正在取消");
            }
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class BackgroundTaskService
    {
        private readonly ObservableCollection<BackgroundTaskItem> _tasks = new();
        public ReadOnlyObservableCollection<BackgroundTaskItem> Tasks { get; }

        public BackgroundTaskService()
        {
            Tasks = new ReadOnlyObservableCollection<BackgroundTaskItem>(_tasks);
            _tasks.CollectionChanged += OnTasksChanged;
        }

        public event EventHandler? Changed;

        public BackgroundTaskItem Enqueue(BackgroundTaskKind kind, string name, string? detail = null)
        {
            var task = new BackgroundTaskItem(kind, name, detail);
            RunOnUi(() => _tasks.Add(task));
            task.PropertyChanged += OnTaskPropertyChanged;
            return task;
        }

        public void RemoveCompleted()
        {
            RunOnUi(() =>
            {
                foreach (var task in _tasks.Where(t => t.Status is BackgroundTaskStatus.Completed or BackgroundTaskStatus.Failed or BackgroundTaskStatus.Canceled).ToList())
                {
                    task.PropertyChanged -= OnTaskPropertyChanged;
                    _tasks.Remove(task);
                }
            });
        }

        public int Count(BackgroundTaskStatus status) => _tasks.Count(t => t.Status == status);
        public int CountQueued => Count(BackgroundTaskStatus.Queued);
        public int CountRunning => Count(BackgroundTaskStatus.Running);
        public int CountCompleted => Count(BackgroundTaskStatus.Completed);
        public int CountFailed => Count(BackgroundTaskStatus.Failed);

        public ReadOnlyObservableCollection<BackgroundTaskItem> Snapshot => Tasks;

        private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }
    }
}
