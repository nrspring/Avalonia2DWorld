using System.Windows.Input;

namespace NWorld.WorldGeneration.App.Infrastructure;

public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return parameter is T typedParameter && (canExecute?.Invoke(typedParameter) ?? true);
    }

    public void Execute(object? parameter)
    {
        if (parameter is T typedParameter)
        {
            execute(typedParameter);
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
