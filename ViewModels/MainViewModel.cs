using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using RemouteAddressBook.Data;
using RemouteAddressBook.Models;
using RemouteAddressBook.Services;

namespace RemouteAddressBook.ViewModels
{
    /// <summary>Модель представления главного окна.</summary>
    public class MainViewModel : ViewModelBase
    {
        public const long AllGroupId = -1;
        public const long UngroupedGroupId = -2;

        private readonly IDialogService _dialogs;

        private Database _database;
        private GroupItem _selectedGroup;
        private LabelItem _selectedLabel;
        private Contact _selectedContact;
        private string _searchText = string.Empty;
        private string _currentGroupTitle = string.Empty;
        private string _statusText = string.Empty;

        public MainViewModel(AppSettings settings, IDialogService dialogs)
        {
            Settings = settings ?? AppSettings.CreateDefault();
            _dialogs = dialogs;

            ContactsView = CollectionViewSource.GetDefaultView(Contacts);
            ContactsView.Filter = FilterContact;

            AddLabelCommand = new RelayCommand(AddLabel);
            EditLabelCommand = new RelayCommand(EditLabel, () => SelectedLabel != null);
            DeleteLabelCommand = new RelayCommand(DeleteLabel, () => SelectedLabel != null);
            ClearLabelFilterCommand = new RelayCommand(() => SelectedLabel = null, () => SelectedLabel != null);

            AddGroupCommand = new RelayCommand(AddGroup);
            EditGroupCommand = new RelayCommand(EditGroup, () => SelectedGroup != null && !SelectedGroup.IsSystem);
            DeleteGroupCommand = new RelayCommand(DeleteGroup, () => SelectedGroup != null && !SelectedGroup.IsSystem);

            AddContactCommand = new RelayCommand(AddContact);
            EditContactCommand = new RelayCommand(EditContact, () => SelectedContact != null);
            DeleteContactCommand = new RelayCommand(DeleteContact, () => SelectedContact != null);

            ResetFiltersCommand = new RelayCommand(ResetFilters);
            ToggleThemeCommand = new RelayCommand(ToggleTheme);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            ImportCommand = new RelayCommand(ImportContacts);
            ConnectCommand = new RelayCommand(parameter => Connect(parameter as string, SelectedContact));
            LaunchToolCommand = new RelayCommand(
                parameter => LaunchTool(parameter as string),
                parameter => CanLaunchTool(parameter as string));

            RefreshToolStatuses();
            OpenDatabase(SettingsService.ResolveDatabasePath(Settings));
        }

        public AppSettings Settings { get; }

        public ObservableCollection<Contact> Contacts { get; } = new ObservableCollection<Contact>();

        public ObservableCollection<GroupItem> Groups { get; } = new ObservableCollection<GroupItem>();

        public ObservableCollection<LabelItem> Labels { get; } = new ObservableCollection<LabelItem>();

        /// <summary>Иконки программ удалённого доступа с признаком «найдена в системе».</summary>
        public ObservableCollection<ToolStatusItem> ToolStatuses { get; } =
            new ObservableCollection<ToolStatusItem>();

        public ICollectionView ContactsView { get; }

        public ICommand AddLabelCommand { get; }

        public ICommand EditLabelCommand { get; }

        public ICommand DeleteLabelCommand { get; }

        public ICommand ClearLabelFilterCommand { get; }

        public ICommand AddGroupCommand { get; }

        public ICommand EditGroupCommand { get; }

        public ICommand DeleteGroupCommand { get; }

        public ICommand AddContactCommand { get; }

        public ICommand EditContactCommand { get; }

        public ICommand DeleteContactCommand { get; }

        public ICommand ResetFiltersCommand { get; }

        public ICommand OpenSettingsCommand { get; }

        public ICommand ImportCommand { get; }

        public ICommand ConnectCommand { get; }

        public ICommand ToggleThemeCommand { get; }

        /// <summary>Запуск программы по клику на её иконке.</summary>
        public ICommand LaunchToolCommand { get; }

        /// <summary>Версия берётся из сборки, чтобы не дублировать её в коде.</summary>
        public string Version
        {
            get
            {
                var version = typeof(MainViewModel).Assembly.GetName().Version;
                return version == null ? string.Empty : version.ToString(3);
            }
        }

        /// <summary>Режим оформления: System / Light / Dark.</summary>
        public AppTheme Theme
        {
            get => Settings.Theme;
            set
            {
                if (Settings.Theme == value)
                {
                    return;
                }

                Settings.Theme = value;
                ThemeService.Apply(value);
                SettingsService.Save(Settings);
                OnPropertyChanged();
                RaiseThemeProperties();
            }
        }

        /// <summary>Применена ли сейчас тёмная палитра.</summary>
        public bool IsDarkTheme => ThemeService.Effective == AppTheme.Dark;

        /// <summary>Глиф кнопки переключения темы (солнце в тёмной теме, луна в светлой).</summary>
        public string ThemeGlyph => IsDarkTheme ? "" : "";

        public string ThemeToolTip => IsDarkTheme ? "Светлая тема" : "Тёмная тема";

        public GroupItem SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (Set(ref _selectedGroup, value))
                {
                    if (value != null)
                    {
                        Settings.SelectedGroupId = value.Kind == GroupKind.All
                            ? AllGroupId
                            : value.Kind == GroupKind.Ungrouped ? UngroupedGroupId : value.Id;
                    }

                    RefreshView();
                }
            }
        }

        public LabelItem SelectedLabel
        {
            get => _selectedLabel;
            set
            {
                if (Set(ref _selectedLabel, value))
                {
                    RefreshView();
                }
            }
        }

        public Contact SelectedContact
        {
            get => _selectedContact;
            set => Set(ref _selectedContact, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value))
                {
                    RefreshView();
                }
            }
        }

        public string CurrentGroupTitle
        {
            get => _currentGroupTitle;
            private set => Set(ref _currentGroupTitle, value);
        }

        /// <summary>Короткое сообщение о результате последнего действия (например, копирования ID).</summary>
        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        public bool ShowLabelsPanel
        {
            get => Settings.ShowLabelsPanel;
            set
            {
                if (Settings.ShowLabelsPanel == value)
                {
                    return;
                }

                Settings.ShowLabelsPanel = value;
                OnPropertyChanged();
                RefreshView();
            }
        }

        public bool ShowGroupsPanel
        {
            get => Settings.ShowGroupsPanel;
            set
            {
                if (Settings.ShowGroupsPanel == value)
                {
                    return;
                }

                Settings.ShowGroupsPanel = value;
                OnPropertyChanged();
                RefreshView();
            }
        }

        public bool ShowPasswords
        {
            get => Settings.ShowPasswords;
            set
            {
                if (Settings.ShowPasswords == value)
                {
                    return;
                }

                Settings.ShowPasswords = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Открывает (при необходимости создаёт) базу данных и загружает данные.</summary>
        public void OpenDatabase(string path)
        {
            try
            {
                _database = new Database(path);
                _database.EnsureCreated();
                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось открыть базу данных:\n" + path + "\n\n" + ex.Message, "База данных");
            }
        }

        /// <summary>Перечитывает группы, метки и контакты из БД.</summary>
        public void ReloadAll()
        {
            if (_database == null)
            {
                return;
            }

            var previousGroupId = Settings.SelectedGroupId;
            var previousLabelId = SelectedLabel?.Id;

            Labels.Clear();
            foreach (var label in _database.LoadLabels())
            {
                Labels.Add(label);
            }

            Contacts.Clear();
            foreach (var contact in _database.LoadContacts())
            {
                Contacts.Add(contact);
            }

            RebuildGroups();

            _selectedGroup = Groups.FirstOrDefault(g => GroupKey(g) == previousGroupId) ?? Groups.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedGroup));

            _selectedLabel = previousLabelId.HasValue
                ? Labels.FirstOrDefault(l => l.Id == previousLabelId.Value)
                : null;
            OnPropertyChanged(nameof(SelectedLabel));

            RefreshView();
        }

        /// <summary>
        /// Запуск доступен, только если программа найдена на компьютере.
        /// Проверяем по уже посчитанному состоянию панели иконок, без обращений к диску:
        /// CanExecute вызывается часто.
        /// </summary>
        public bool CanLaunchTool(string toolKey)
        {
            if (string.IsNullOrEmpty(toolKey))
            {
                return false;
            }

            foreach (var status in ToolStatuses)
            {
                if (status.Key == toolKey)
                {
                    return status.IsAvailable;
                }
            }

            return false;
        }

        /// <summary>Запускает программу без подключения — по клику на иконке.</summary>
        public void LaunchTool(string toolKey)
        {
            if (!CanLaunchTool(toolKey))
            {
                return;
            }

            var result = ConnectionService.LaunchTool(toolKey, Settings);
            if (result.Success)
            {
                StatusText = result.Message;
                return;
            }

            StatusText = string.Empty;
            _dialogs?.Error(result.Message, "Запуск программы");
        }

        /// <summary>
        /// Обновляет панель иконок: для каждого инструмента проверяет, найден ли его
        /// исполняемый файл, и вытаскивает иконку.
        /// </summary>
        public void RefreshToolStatuses()
        {
            AppIconService.ClearCache();

            if (ToolStatuses.Count == 0)
            {
                foreach (var key in ToolKeys.All)
                {
                    ToolStatuses.Add(new ToolStatusItem(key, ToolKeys.DisplayName(key)));
                }
            }

            foreach (var item in ToolStatuses)
            {
                var tool = Settings.GetTool(item.Key);
                var resolved = ToolDiscoveryService.ResolveExecutable(tool.ExePath);

                item.IsAvailable = resolved != null;
                item.ExePath = resolved ?? tool.ExePath;

                // Если файла нет, иконку взять неоткуда — показывается бледная заглушка.
                item.Icon = resolved == null ? null : AppIconService.GetIcon(resolved);
            }

            // Доступность кнопок запуска изменилась — пересчитать CanExecute.
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Запускает подключение указанным инструментом.</summary>
        public void Connect(string toolKey, Contact contact)
        {
            if (string.IsNullOrEmpty(toolKey) || contact == null)
            {
                return;
            }

            var result = ConnectionService.Connect(toolKey, contact, Settings);
            if (result.Success)
            {
                StatusText = result.Message;
                return;
            }

            StatusText = string.Empty;
            _dialogs?.Error(result.Message, "Подключение");
        }

        private static long GroupKey(GroupItem group)
        {
            switch (group.Kind)
            {
                case GroupKind.All: return AllGroupId;
                case GroupKind.Ungrouped: return UngroupedGroupId;
                default: return group.Id;
            }
        }

        private void RebuildGroups()
        {
            var dbGroups = _database.LoadGroups();
            var counts = new Dictionary<long, int>();
            var ungrouped = 0;
            foreach (var contact in Contacts)
            {
                if (contact.GroupId.HasValue)
                {
                    counts.TryGetValue(contact.GroupId.Value, out var current);
                    counts[contact.GroupId.Value] = current + 1;
                }
                else
                {
                    ungrouped++;
                }
            }

            Groups.Clear();
            Groups.Add(new GroupItem(AllGroupId, "Все", GroupKind.All) { Count = Contacts.Count });
            Groups.Add(new GroupItem(UngroupedGroupId, "Без группы", GroupKind.Ungrouped) { Count = ungrouped });
            foreach (var group in dbGroups)
            {
                group.Count = counts.TryGetValue(group.Id, out var count) ? count : 0;
                Groups.Add(group);
            }
        }

        private void RefreshView()
        {
            ContactsView.Refresh();
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            var name = SelectedGroup?.Name ?? "Все";
            var visible = ContactsView.Cast<object>().Count();
            CurrentGroupTitle = name + " (" + visible + ")";
        }

        private bool FilterContact(object item)
        {
            if (item is not Contact contact)
            {
                return false;
            }

            // Фильтр по группе (только если панель групп включена).
            if (ShowGroupsPanel && SelectedGroup != null)
            {
                if (SelectedGroup.Kind == GroupKind.Ungrouped && contact.GroupId.HasValue)
                {
                    return false;
                }

                if (SelectedGroup.Kind == GroupKind.Normal && contact.GroupId != SelectedGroup.Id)
                {
                    return false;
                }
            }

            // Фильтр по метке (только если панель меток включена). Работает независимо от групп.
            if (ShowLabelsPanel && SelectedLabel != null && !contact.LabelIds.Contains(SelectedLabel.Id))
            {
                return false;
            }

            // Текстовый поиск поверх остальных фильтров.
            var search = SearchText;
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            search = search.Trim();
            return Contains(contact.Name, search)
                   || Contains(contact.Comment, search)
                   || Contains(contact.AnyDesk, search)
                   || Contains(contact.Rudesktop, search)
                   || Contains(contact.Assistant, search)
                   || Contains(contact.Ammyy, search)
                   || Contains(contact.Rdp, search)
                   || Contains(contact.RdpLogin, search);
        }

        private static bool Contains(string value, string search) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private void ToggleTheme()
        {
            Theme = IsDarkTheme ? AppTheme.Light : AppTheme.Dark;
        }

        private void RaiseThemeProperties()
        {
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(ThemeGlyph));
            OnPropertyChanged(nameof(ThemeToolTip));
        }

        private void ResetFilters()
        {
            SelectedLabel = null;
            SelectedGroup = Groups.FirstOrDefault(g => g.Kind == GroupKind.All);
            SearchText = string.Empty;
        }

        private void AddLabel()
        {
            var name = _dialogs?.PromptText("Новая метка", "Название метки:", string.Empty);
            if (string.IsNullOrWhiteSpace(name) || _database == null)
            {
                return;
            }

            try
            {
                var id = _database.AddLabel(name.Trim());
                Labels.Add(new LabelItem(id, name.Trim()));
                SortLabels();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось создать метку: " + ex.Message, "Метки");
            }
        }

        private void EditLabel()
        {
            var label = SelectedLabel;
            if (label == null)
            {
                return;
            }

            var name = _dialogs?.PromptText("Переименовать метку", "Название метки:", label.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                _database.RenameLabel(label.Id, name.Trim());
                label.Name = name.Trim();
                SortLabels();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось переименовать метку: " + ex.Message, "Метки");
            }
        }

        private void DeleteLabel()
        {
            var label = SelectedLabel;
            if (label == null || _dialogs == null)
            {
                return;
            }

            if (!_dialogs.Confirm("Удалить метку «" + label.Name + "»?", "Метки"))
            {
                return;
            }

            try
            {
                _database.DeleteLabel(label.Id);
                foreach (var contact in Contacts)
                {
                    contact.LabelIds.Remove(label.Id);
                }

                SelectedLabel = null;
                Labels.Remove(label);
                RefreshView();
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось удалить метку: " + ex.Message, "Метки");
            }
        }

        private void SortLabels()
        {
            var sorted = Labels.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var selected = SelectedLabel;
            Labels.Clear();
            foreach (var label in sorted)
            {
                Labels.Add(label);
            }

            _selectedLabel = selected != null && sorted.Contains(selected) ? selected : null;
            OnPropertyChanged(nameof(SelectedLabel));
        }

        private void AddGroup()
        {
            var name = _dialogs?.PromptText("Новая группа", "Название группы:", string.Empty);
            if (string.IsNullOrWhiteSpace(name) || _database == null)
            {
                return;
            }

            try
            {
                _database.AddGroup(name.Trim());
                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось создать группу: " + ex.Message, "Группы");
            }
        }

        private void EditGroup()
        {
            var group = SelectedGroup;
            if (group == null || group.IsSystem)
            {
                return;
            }

            var name = _dialogs?.PromptText("Переименовать группу", "Название группы:", group.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                _database.RenameGroup(group.Id, name.Trim());
                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось переименовать группу: " + ex.Message, "Группы");
            }
        }

        private void DeleteGroup()
        {
            var group = SelectedGroup;
            if (group == null || group.IsSystem || _dialogs == null)
            {
                return;
            }

            if (!_dialogs.Confirm(
                    "Удалить группу «" + group.Name + "»?\nКонтакты группы останутся, но станут «Без группы».",
                    "Группы"))
            {
                return;
            }

            try
            {
                _database.DeleteGroup(group.Id);
                Settings.SelectedGroupId = AllGroupId;
                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось удалить группу: " + ex.Message, "Группы");
            }
        }

        private void AddContact()
        {
            if (_dialogs == null || _database == null)
            {
                return;
            }

            var contact = new Contact();
            if (SelectedGroup != null && SelectedGroup.Kind == GroupKind.Normal)
            {
                contact.GroupId = SelectedGroup.Id;
            }

            if (!_dialogs.EditContact(contact, UserGroups(), Labels, "Новый контакт"))
            {
                return;
            }

            try
            {
                _database.InsertContact(contact);
                ReloadAll();
                SelectedContact = Contacts.FirstOrDefault(c => c.Id == contact.Id);
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось сохранить контакт: " + ex.Message, "Контакты");
            }
        }

        private void EditContact()
        {
            var contact = SelectedContact;
            if (contact == null || _dialogs == null || _database == null)
            {
                return;
            }

            var draft = contact.Clone();
            if (!_dialogs.EditContact(draft, UserGroups(), Labels, "Изменить контакт"))
            {
                return;
            }

            try
            {
                _database.UpdateContact(draft);
                contact.CopyValuesFrom(draft);
                RebuildGroups();
                RestoreSelectedGroup();
                RefreshView();
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось сохранить контакт: " + ex.Message, "Контакты");
            }
        }

        private void DeleteContact()
        {
            var contact = SelectedContact;
            if (contact == null || _dialogs == null || _database == null)
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(contact.Name) ? "(без имени)" : contact.Name;
            if (!_dialogs.Confirm("Удалить контакт «" + name + "»?", "Контакты"))
            {
                return;
            }

            try
            {
                _database.DeleteContact(contact.Id);
                Contacts.Remove(contact);
                RebuildGroups();
                RestoreSelectedGroup();
                RefreshView();
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось удалить контакт: " + ex.Message, "Контакты");
            }
        }

        private void RestoreSelectedGroup()
        {
            var target = Settings.SelectedGroupId;
            _selectedGroup = Groups.FirstOrDefault(g => GroupKey(g) == target) ?? Groups.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedGroup));
        }

        private IEnumerable<GroupItem> UserGroups() => Groups.Where(g => !g.IsSystem).ToList();

        private void OpenSettings()
        {
            if (_dialogs == null)
            {
                return;
            }

            var previousPath = SettingsService.ResolveDatabasePath(Settings);
            if (!_dialogs.EditSettings(Settings))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowLabelsPanel));
            OnPropertyChanged(nameof(ShowGroupsPanel));
            OnPropertyChanged(nameof(Theme));

            // Тема и пути к программам могли измениться в окне настроек.
            ThemeService.Apply(Settings.Theme);
            RaiseThemeProperties();
            RefreshToolStatuses();
            SettingsService.Save(Settings);

            var newPath = SettingsService.ResolveDatabasePath(Settings);
            if (!string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                OpenDatabase(newPath);
            }
            else
            {
                RefreshView();
            }
        }

        private void ImportContacts()
        {
            if (_dialogs == null || _database == null)
            {
                return;
            }

            if (_dialogs.ImportContacts(_database, Contacts.ToList(), UserGroups()))
            {
                ReloadAll();
            }
        }
    }
}
