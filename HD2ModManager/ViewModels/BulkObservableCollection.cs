using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

// 作用：以单次 Reset 通知替换列表内容，避免大量逐项 CollectionChanged 消息阻塞 UI 线程。
namespace HD2ModManager.ViewModels
{
    public sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotifications;

        public void ReplaceWith(IEnumerable<T> items)
        {
            _suppressNotifications = true;
            try
            {
                Items.Clear();
                foreach (var item in items) Items.Add(item);
            }
            finally
            {
                _suppressNotifications = false;
            }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotifications) base.OnCollectionChanged(e);
        }
    }
}