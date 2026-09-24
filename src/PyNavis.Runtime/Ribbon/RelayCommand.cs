using System;
using System.Windows.Input;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>ICommand adapter for AdWindows ribbon buttons.</summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;

        public RelayCommand(Action<object> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            try
            {
                _execute(parameter);
            }
            catch (Exception ex)
            {
                Log.Error("Unhandled exception in ribbon command", ex);
            }
        }
    }
}
