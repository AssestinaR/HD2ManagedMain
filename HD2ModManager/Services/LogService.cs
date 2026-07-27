using System;
using System.IO;

namespace HD2ModManager.Services
{
    public static class LogService
    {
        private static readonly object _lock = new object();
        private static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", $"manager_{DateTime.Now:yyyyMMdd}.log");

        public static void Info(string message)
        {
            Write("INFO", message);
        }
        public static void Error(string message)
        {
            Write("ERROR", message);
        }
        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                var path = LogPath;
                var dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var line = $"[{DateTimeOffset.Now:O}] {level} {message}";
                lock (_lock)
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
