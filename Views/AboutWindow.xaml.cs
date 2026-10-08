using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using RemoteAccessAddressBook.Services;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>Окно «О приложении»: версия и расположение файлов данных.</summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow(string version, string databasePath)
        {
            InitializeComponent();

            VersionText.Text = "Версия " + version;
            DatabasePathBox.Text = databasePath ?? string.Empty;
            SettingsPathBox.Text = SettingsService.SettingsPath;
            RuntimeText.Text = RuntimeInformation.FrameworkDescription + ", " +
                               RuntimeInformation.OSDescription.Trim();
        }

        private void Repository_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть браузер: " + ex.Message, "О приложении",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            e.Handled = true;
        }

        private void OpenDatabaseFolder_Click(object sender, RoutedEventArgs e) => ShowInExplorer(DatabasePathBox.Text);

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => ShowInExplorer(SettingsPathBox.Text);

        /// <summary>Открывает проводник с выделенным файлом, а если файла ещё нет — его каталог.</summary>
        private void ShowInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                    return;
                }

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start("explorer.exe", "\"" + directory + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку: " + ex.Message, Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
