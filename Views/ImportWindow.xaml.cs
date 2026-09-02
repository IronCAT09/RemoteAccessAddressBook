using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using RemouteAddressBook.Data;
using RemouteAddressBook.Models;
using RemouteAddressBook.Services;
using RemouteAddressBook.ViewModels;

namespace RemouteAddressBook.Views
{
    /// <summary>Окно импорта контактов из CSV/XLSX.</summary>
    public partial class ImportWindow : Window
    {
        private const long NoGroupId = -2;

        private readonly Database _database;
        private readonly IList<Contact> _existingContacts;
        private readonly ObservableCollection<ImportMappingRow> _mapping = new ObservableCollection<ImportMappingRow>();

        private ImportTable _table;

        public ImportWindow(Database database, IList<Contact> existingContacts, IEnumerable<GroupItem> groups)
        {
            InitializeComponent();

            _database = database;
            _existingContacts = existingContacts ?? new List<Contact>();

            var groupChoices = new List<GroupItem>
            {
                new GroupItem(NoGroupId, "— Без группы —", GroupKind.Ungrouped),
            };
            if (groups != null)
            {
                groupChoices.AddRange(groups.Where(g => !g.IsSystem));
            }

            GroupComboBox.ItemsSource = groupChoices;
            GroupComboBox.SelectedItem = groupChoices[0];

            MappingList.ItemsSource = _mapping;
        }

        /// <summary>true, если контакты были импортированы.</summary>
        public bool Imported { get; private set; }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Файл с контактами",
                Filter = "Таблицы (*.csv;*.xlsx)|*.csv;*.xlsx|CSV (*.csv)|*.csv|Excel (*.xlsx)|*.xlsx|Все файлы (*.*)|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            LoadFile(dialog.FileName);
        }

        private void LoadFile(string path)
        {
            try
            {
                var delimiter = ImportService.DetectDelimiter(path);
                _table = ImportService.ReadTable(path, delimiter);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось прочитать файл:\n" + ex.Message, "Импорт",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            FilePathBox.Text = path;
            RowCountText.Text = "Строк с данными: " + _table.Rows.Count;

            var columns = new List<ColumnChoice> { new ColumnChoice(-1, ImportService.DisplayName(ContactField.None)) };
            for (var i = 0; i < _table.Headers.Count; i++)
            {
                columns.Add(new ColumnChoice(i, _table.Headers[i]));
            }

            _mapping.Clear();
            foreach (ContactField field in Enum.GetValues(typeof(ContactField)))
            {
                if (field == ContactField.None)
                {
                    continue;
                }

                var row = new ImportMappingRow(field, columns);
                row.SelectedColumn = GuessColumn(field, columns);
                _mapping.Add(row);
            }

            ImportButton.IsEnabled = _table.Rows.Count > 0;
        }

        /// <summary>Пытается сопоставить поле с колонкой по названию заголовка.</summary>
        private static ColumnChoice GuessColumn(ContactField field, IList<ColumnChoice> columns)
        {
            var keywords = Keywords(field);
            foreach (var column in columns)
            {
                if (column.Index < 0)
                {
                    continue;
                }

                var header = (column.Name ?? string.Empty).Trim().ToLowerInvariant();
                foreach (var keyword in keywords)
                {
                    if (header == keyword)
                    {
                        return column;
                    }
                }
            }

            return columns[0];
        }

        private static string[] Keywords(ContactField field)
        {
            switch (field)
            {
                case ContactField.Name: return new[] { "имя", "name", "название", "клиент" };
                case ContactField.Comment: return new[] { "комментарий", "comment", "примечание" };
                case ContactField.AnyDesk: return new[] { "anydesk", "энидеск", "any desk" };
                case ContactField.Rudesktop: return new[] { "rudesktop", "ru desktop", "рудесктоп" };
                case ContactField.Assistant: return new[] { "ассистент", "assistant" };
                case ContactField.Ammyy: return new[] { "ammyy", "ammyyadmin", "ammyy admin" };
                case ContactField.Rdp: return new[] { "rdp", "адрес", "address", "adress", "host" };
                case ContactField.Password: return new[] { "пароль", "password", "pass" };
                case ContactField.RdpLogin: return new[] { "rdp логин", "логин", "login", "user" };
                case ContactField.RdpPassword: return new[] { "rdp пароль", "rdp password" };
                default: return Array.Empty<string>();
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_table == null)
            {
                return;
            }

            var mapping = new Dictionary<ContactField, int>();
            foreach (var row in _mapping)
            {
                if (row.SelectedColumn != null && row.SelectedColumn.Index >= 0)
                {
                    mapping[row.Field] = row.SelectedColumn.Index;
                }
            }

            if (mapping.Count == 0)
            {
                MessageBox.Show(this, "Не выбрано ни одной колонки для импорта.", "Импорт",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedGroup = GroupComboBox.SelectedItem as GroupItem;
            var groupId = selectedGroup == null || selectedGroup.Kind != GroupKind.Normal
                ? (long?)null
                : selectedGroup.Id;

            ImportReport report;
            try
            {
                report = ImportService.Import(_table, mapping, groupId, _existingContacts, _database);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ошибка импорта:\n" + ex.Message, "Импорт",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Imported = Imported || report.Added > 0;

            var message = "Добавлено: " + report.Added +
                          "\nПропущено как дубликаты: " + report.Duplicates +
                          "\nОшибок: " + report.Errors;
            if (report.ErrorMessages.Count > 0)
            {
                message += "\n\n" + string.Join("\n", report.ErrorMessages);
            }

            MessageBox.Show(this, message, "Отчёт об импорте", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = Imported;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = Imported;
            Close();
        }
    }
}
