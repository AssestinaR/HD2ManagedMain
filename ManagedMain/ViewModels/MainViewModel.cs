using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ManagedMain.Models;
using ManagedMain.Services;

namespace ManagedMain.ViewModels
{
    // 简化旧绑定的包装，内部转发到 ManagedMainViewModel
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ManagedMainViewModel _inner = new();
        public ObservableCollection<ProfileEntry> Profiles => _inner.Profiles;
        public ICommand SaveCommand => _inner.SaveOptionsCommand;
        public ICommand AddProfileCommand => _inner.NewProfileCommand;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _exec;
        private readonly Func<object?, bool>? _can;
        public RelayCommand(Action<object?> exec, Func<object?, bool>? can = null)
        { _exec = exec; _can = can; }
        public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _exec(parameter);
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
