using System;
using System.Collections.Concurrent;
using System.Text;

namespace LiberTeaManager.Services
{
    /// <summary>
    /// 批量缓冲日志，减少频繁 UI AppendText 调用导致的卡顿
    /// </summary>
    internal sealed class BufferedLogService : ILogService
    {
        private readonly ConcurrentQueue<string> _queue = new();
        private readonly int _flushThreshold;
        private readonly object _sync = new();
        private Action<string>? _immediateFallback;

        public BufferedLogService(int flushThreshold = 64, Action<string>? immediateFallback = null)
        {
            _flushThreshold = Math.Max(8, flushThreshold);
            _immediateFallback = immediateFallback;
        }

        public void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _queue.Enqueue(message);
            if (_queue.Count >= _flushThreshold)
            {
                // 尝试自动刷新
                Flush(_immediateFallback);
            }
        }

        /// <summary>
        /// 手动刷新到外部 sink。若 sink 为空则丢弃（安全）。
        /// </summary>
        public void Flush(Action<string>? sink)
        {
            if (sink == null) return;
            lock (_sync)
            {
                if (_queue.IsEmpty) return;
                var sb = new StringBuilder();
                while (_queue.TryDequeue(out var line))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    sb.AppendLine(line);
                }
                if (sb.Length > 0)
                {
                    // 分块防止一次性超大（极端情况下）
                    var text = sb.ToString();
                    const int chunk = 4096;
                    if (text.Length <= chunk)
                        sink(text.TrimEnd('\n'));
                    else
                    {
                        int idx = 0;
                        while (idx < text.Length)
                        {
                            int len = Math.Min(chunk, text.Length - idx);
                            sink(text.Substring(idx, len));
                            idx += len;
                        }
                    }
                }
            }
        }

        public void SetImmediateFallback(Action<string> sink) => _immediateFallback = sink;
    }
}
