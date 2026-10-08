using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RemoteAccessAddressBook.Models;
using RemoteAccessAddressBook.Services;
using RemoteAccessAddressBook.ViewModels;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>Окно настроек приложения.</summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ObservableCollection<ToolSettingRow> _tools = new ObservableCollection<ToolSettingRow>();

        private AppTheme _originalTheme;
        private bool _themeInitialized;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();

            _settings = settings;
            _settings.ApplyToolDefaults();

            ShowLabelsCheckBox.IsChecked = settings.ShowLabelsPanel;
            ShowGroupsCheckBox.IsChecked = settings.ShowGroupsPanel;
            MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTray;
            DatabasePathBox.Text = settings.DatabasePath;

            _originalTheme = settings.Theme;
            ThemeSystemRadio.IsChecked = settings.Theme == AppTheme.System;
            ThemeLightRadio.IsChecked = settings.Theme == AppTheme.Light;
            ThemeDarkRadio.IsChecked = settings.Theme == AppTheme.Dark;
            _themeInitialized = true;

            foreach (var key in ToolKeys.All)
            {
                var config = settings.GetTool(key);
                _tools.Add(new ToolSettingRow(key, ToolKeys.DisplayName(key))
                {
                    ExePath = config.ExePath,
                    Arguments = config.Arguments,
                    PasswordToStdin = config.PasswordToStdin,
                    UseCmdKey = config.UseCmdKey,
                    CopyIdToClipboard = config.CopyIdToClipboard,
                });
            }

            ToolsList.ItemsSource = _tools;
        }

        private void BrowseDatabase_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Файл базы данных",
                Filter = "База данных SQLite (*.db)|*.db|Все файлы (*.*)|*.*",
                FileName = string.IsNullOrWhiteSpace(DatabasePathBox.Text)
                    ? "addressbook.db"
                    : DatabasePathBox.Text,
                OverwritePrompt = false,
                CheckPathExists = true,
            };

            if (dialog.ShowDialog(this) == true)
            {
                DatabasePathBox.Text = dialog.FileName;
            }
        }

        private void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ToolSettingRow row)
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Программа для «" + row.DisplayName + "»",
                Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) == true)
            {
                row.ExePath = dialog.FileName;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Закрытие крестиком равносильно отмене: откатываем предпросмотр темы.
            if (DialogResult != true && ThemeService.Current != _originalTheme)
            {
                ThemeService.Apply(_originalTheme);
            }

            base.OnClosing(e);
        }

        /// <summary>Тема применяется сразу, чтобы результат был виден до сохранения.</summary>
        private void Theme_Changed(object sender, RoutedEventArgs e)
        {
            if (!_themeInitialized)
            {
                return;
            }

            ThemeService.Apply(SelectedTheme());
        }

        private AppTheme SelectedTheme()
        {
            if (ThemeDarkRadio.IsChecked == true)
            {
                return AppTheme.Dark;
            }

            return ThemeSystemRadio.IsChecked == true ? AppTheme.System : AppTheme.Light;
        }

        /// <summary>Ищет установленные программы и подставляет найденные пути.</summary>
        private void Discover_Click(object sender, RoutedEventArgs e)
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            Dictionary<string, string> found;
            try
            {
                found = ToolDiscoveryService.DiscoverAll();
            }
            finally
            {
                Cursor = null;
            }

            var missing = new List<ToolSettingRow>();
            foreach (var row in _tools)
            {
                if (!ToolDiscoveryService.IsUsablePath(row.ExePath))
                {
                    missing.Add(row);
                }
            }

            // Если все пути уже рабочие — предлагаем перезаписать их найденными.
            var rowsToFill = missing;
            if (missing.Count == 0)
            {
                var answer = MessageBox.Show(
                    this,
                    "Все пути уже заданы и файлы существуют.\nПерезаписать их найденными в системе?",
                    "Поиск программ",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }

                rowsToFill = new List<ToolSettingRow>(_tools);
            }

            var report = new StringBuilder();
            var updated = 0;
            foreach (var row in rowsToFill)
            {
                if (found.TryGetValue(row.Key, out var path) &&
                    !string.Equals(path, row.ExePath, StringComparison.OrdinalIgnoreCase))
                {
                    row.ExePath = path;
                    updated++;
                    report.AppendLine(row.DisplayName + ": " + path);
                }
            }

            foreach (var row in rowsToFill)
            {
                if (!found.ContainsKey(row.Key))
                {
                    report.AppendLine(row.DisplayName + ": не найдено");
                }
            }

            DiscoverStatusText.Text = updated > 0
                ? "Найдено и подставлено: " + updated
                : "Новых путей не найдено";

            MessageBox.Show(
                this,
                (updated > 0 ? "Подставлено путей: " + updated : "Ничего не подставлено") +
                "\n\n" + report.ToString().TrimEnd(),
                "Поиск программ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.Theme = SelectedTheme();
            _settings.ShowLabelsPanel = ShowLabelsCheckBox.IsChecked == true;
            _settings.ShowGroupsPanel = ShowGroupsCheckBox.IsChecked == true;
            _settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
            _settings.DatabasePath = DatabasePathBox.Text.Trim();

            foreach (var row in _tools)
            {
                _settings.Tools[row.Key] = new ToolConfig
                {
                    ExePath = row.ExePath?.Trim() ?? string.Empty,
                    Arguments = row.Arguments?.Trim() ?? string.Empty,
                    PasswordToStdin = row.PasswordToStdin,
                    UseCmdKey = row.UseCmdKey,
                    CopyIdToClipboard = row.CopyIdToClipboard,
                };
            }

            SettingsService.Save(_settings);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Возвращаем тему, которая была до открытия окна.
            if (ThemeService.Current != _originalTheme)
            {
                ThemeService.Apply(_originalTheme);
            }

            DialogResult = false;
            Close();
        }
    }
}
