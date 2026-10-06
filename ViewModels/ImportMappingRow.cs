using System.Collections.Generic;
using RemoteAccessAddressBook.Services;

namespace RemoteAccessAddressBook.ViewModels
{
    /// <summary>Вариант выбора колонки файла при импорте.</summary>
    public class ColumnChoice
    {
        public ColumnChoice(int index, string name)
        {
            Index = index;
            Name = name;
        }

        /// <summary>Индекс колонки в файле; -1 — «не импортировать».</summary>
        public int Index { get; }

        public string Name { get; }
    }

    /// <summary>Строка маппинга «поле контакта → колонка файла».</summary>
    public class ImportMappingRow : ViewModelBase
    {
        private ColumnChoice _selectedColumn;

        public ImportMappingRow(ContactField field, IReadOnlyList<ColumnChoice> columns)
        {
            Field = field;
            DisplayName = ImportService.DisplayName(field);
            Columns = columns;
        }

        public ContactField Field { get; }

        public string DisplayName { get; }

        public IReadOnlyList<ColumnChoice> Columns { get; }

        public ColumnChoice SelectedColumn
        {
            get => _selectedColumn;
            set => Set(ref _selectedColumn, value);
        }
    }
}
