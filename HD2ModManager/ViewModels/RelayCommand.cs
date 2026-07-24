using System;
using System.Windows.Input;

namespace HD2ModManager.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        public RelayCommand(Action execute) : this(_ => execute(), null) { }
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            _ = dispatcher.InvokeAsync(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
