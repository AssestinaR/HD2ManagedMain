using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ManagedMain.Services
{
    public interface ILogService
    {
        ObservableCollection<string> Lines { get; }
        string Latest { get; }
        LogLevel LatestLevel { get; }
        void Log(string message);
        void Log(LogLevel level, string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Debug(string message);
    }

    public class LogService : ILogService, INotifyPropertyChanged
    {
        public ObservableCollection<string> Lines { get; } = new();

        private string _latest = string.Empty;
        public string Latest { get => _latest; private set { if (_latest != value) { _latest = value; OnPropertyChanged(); } } }

        private LogLevel _latestLevel = LogLevel.Info;
        public LogLevel LatestLevel { get => _latestLevel; private set { if (_latestLevel != value) { _latestLevel = value; OnPropertyChanged(); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Log(string message) => Log(LogLevel.Info, message);

        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warn(string message) => Log(LogLevel.Warn, message);
        public void Error(string message) => Log(LogLevel.Error, message);
        public void Debug(string message) => Log(LogLevel.Debug, message);

        public void Log(LogLevel level, string message)
        {
            var prefix = level switch
            {
                LogLevel.Error => "[ERROR] ",
                LogLevel.Warn => "[WARN ] ",
                LogLevel.Debug => "[DEBUG] ",
                _ => "[INFO ] "
            };
            var line = $"[{DateTime.Now:HH:mm:ss}] {prefix}{message}";
            try
            {
                if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                {
                    Lines.Add(line);
                    Latest = line;
                    LatestLevel = level;
                }
                else
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() => { Lines.Add(line); Latest = line; LatestLevel = level; });
                }
            }
            catch
            {
                Lines.Add(line);
                Latest = line;
                LatestLevel = level;
            }
        }
    }
}
