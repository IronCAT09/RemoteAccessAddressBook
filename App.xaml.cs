using System;
using System.Windows;
using System.Windows.Threading;

namespace RemoteAccessAddressBook
{
    /// <summary>Точка входа приложения.</summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                "Непредвиденная ошибка:" + Environment.NewLine + e.Exception.Message,
                "Remote Access — Address Book",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
