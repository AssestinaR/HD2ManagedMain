using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace HD2ModManager.Services
{
    public enum ImportTaskStatus { Queued, Running, Done, Failed }

    public class ImportTaskItem
    {
        public string Path { get; set; } = string.Empty;
        public ImportTaskStatus Status { get; set; } = ImportTaskStatus.Queued;
        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ImportQueueService
    {
        private readonly ObservableCollection<ImportTaskItem> _tasks = new();
        public ReadOnlyObservableCollection<ImportTaskItem> Tasks { get; }

        public ImportQueueService()
        {
            Tasks = new ReadOnlyObservableCollection<ImportTaskItem>(_tasks);
        }

        public void Enqueue(params string[] paths)
        {
            foreach (var p in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                _tasks.Add(new ImportTaskItem { Path = p });
            }
        }

        public void MarkRunning(ImportTaskItem item)
        {
            item.Status = ImportTaskStatus.Running;
        }

        public void MarkDone(ImportTaskItem item)
        {
            item.Status = ImportTaskStatus.Done;
            item.Error = null;
        }

        public void MarkFailed(ImportTaskItem item, string error)
        {
            item.Status = ImportTaskStatus.Failed;
            item.Error = error;
        }

        public int CountQueued => _tasks.Count(t => t.Status == ImportTaskStatus.Queued);
        public int CountRunning => _tasks.Count(t => t.Status == ImportTaskStatus.Running);
        public int CountFailed => _tasks.Count(t => t.Status == ImportTaskStatus.Failed);
        public int CountDone => _tasks.Count(t => t.Status == ImportTaskStatus.Done);
    }
}
