using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using RemoteAccessAddressBook.Data;
using RemoteAccessAddressBook.Models;
using RemoteAccessAddressBook.Services;

namespace RemoteAccessAddressBook.ViewModels
{
    /// <summary>
    /// Модель представления главного окна. Показывает контакты двух баз сразу: облачной (сервер)
    /// и локальной/сетевой (SQLite). Сначала облачные, затем локальные; дубли локальной
    /// копии скрываются, расхождения помечаются. Группы и метки обеих баз сопоставляются по имени.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        public const long AllGroupId = -1;
        public const long UngroupedGroupId = -2;

        /// <summary>Как часто проверять, не изменили ли базы на других компьютерах.</summary>
        private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(5);

        private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;

        private readonly IDialogService _dialogs;
        private readonly DispatcherTimer _syncTimer;

        private Database _database;
        private CloudStore _cloud;
        private long _knownRevision = -1;
        private bool _checking;
        private bool _busy;
        private bool _databaseUnavailable;
        private bool _localLocked;
        private bool _localEncrypted;
        private GroupItem _selectedGroup;
        private LabelItem _selectedLabel;
        private Contact _selectedContact;
        private string _searchText = string.Empty;
        private string _currentGroupTitle = string.Empty;
        private string _statusText = string.Empty;

        public MainViewModel(AppSettings settings, IDialogService dialogs)
        {
            Settings = settings ?? AppSettings.CreateDefault();
            Settings.Cloud ??= new CloudSettings();
            Settings.LocalKeys ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            CopyToCloudCommand = new RelayCommand(
                () => CopyToOther(SelectedContact),
                () => SelectedContact != null && !SelectedContact.IsCloud && _cloud != null && _cloud.CanWrite);
            CopyToLocalCommand = new RelayCommand(
                () => CopyToOther(SelectedContact),
                () => SelectedContact != null && SelectedContact.IsCloud && _database != null && !_localLocked);
            ResolveConflictCommand = new RelayCommand(
                () => ResolveConflict(SelectedContact),
                () => SelectedContact != null && SelectedContact.HasConflict);

            ResetFiltersCommand = new RelayCommand(ResetFilters);
            ToggleThemeCommand = new RelayCommand(ToggleTheme);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            ImportCommand = new RelayCommand(ImportContacts);
            RefreshCommand = new RelayCommand(Refresh);
            AboutCommand = new RelayCommand(() => _dialogs?.ShowAbout(Version, SettingsService.ResolveDatabasePath(Settings)));
            ConnectCommand = new RelayCommand(parameter => Connect(parameter as string, SelectedContact));
            LaunchToolCommand = new RelayCommand(
                parameter => LaunchTool(parameter as string),
                parameter => CanLaunchTool(parameter as string));

            CloudConnectCommand = new RelayCommand(ConnectCloud);
            CloudDisconnectCommand = new RelayCommand(DisconnectCloud, () => Settings.Cloud.IsConfigured);
            SyncCloudToLocalCommand = new RelayCommand(
                () => Synchronize(toCloud: false),
                () => _cloud != null && _cloud.Contacts.Count > 0 && _database != null && !_localLocked);
            SyncLocalToCloudCommand = new RelayCommand(
                () => Synchronize(toCloud: true),
                () => _cloud != null && _cloud.CanWrite && _database != null && !_localLocked);
            LocalEncryptionCommand = new RelayCommand(ToggleLocalEncryption, () => _database != null);

            RefreshToolStatuses();
            _cloud = new CloudStore(Settings.Cloud);
            OpenDatabase(SettingsService.ResolveDatabasePath(Settings));

            _syncTimer = new DispatcherTimer { Interval = SyncInterval };
            _syncTimer.Tick += async (_, _) => await CheckForExternalChangesAsync();
            _syncTimer.Start();

            // Первую выборку из облака не ждём таймера.
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () => await CheckForExternalChangesAsync()));
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

        /// <summary>Скопировать выбранный локальный контакт в облако.</summary>
        public ICommand CopyToCloudCommand { get; }

        /// <summary>Скопировать выбранный облачный контакт в локальную базу.</summary>
        public ICommand CopyToLocalCommand { get; }

        /// <summary>Разобрать расхождение между облачной и локальной версией контакта.</summary>
        public ICommand ResolveConflictCommand { get; }

        public ICommand ResetFiltersCommand { get; }

        public ICommand OpenSettingsCommand { get; }

        public ICommand ImportCommand { get; }

        /// <summary>Перечитать базы (F5) — например, чтобы сразу увидеть правки коллег.</summary>
        public ICommand RefreshCommand { get; }

        public ICommand AboutCommand { get; }

        public ICommand ConnectCommand { get; }

        public ICommand ToggleThemeCommand { get; }

        /// <summary>Запуск программы по клику на её иконке.</summary>
        public ICommand LaunchToolCommand { get; }

        public ICommand CloudConnectCommand { get; }

        public ICommand CloudDisconnectCommand { get; }

        public ICommand SyncCloudToLocalCommand { get; }

        public ICommand SyncLocalToCloudCommand { get; }

        public ICommand LocalEncryptionCommand { get; }

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

        /// <summary>Подпись у кнопки облака в шапке: пользователь или что требуется.</summary>
        public string CloudStatusText => _cloud == null ? string.Empty : _cloud.State switch
        {
            CloudState.Off => string.Empty,
            CloudState.Ready => Settings.Cloud.Login,
            CloudState.Offline => Settings.Cloud.Login + " (нет связи)",
            CloudState.NeedsLogin => "нужен вход",
            CloudState.NeedsPassphrase => "нужна фраза",
            _ => string.Empty,
        };

        public string CloudToolTip
        {
            get
            {
                if (_cloud == null || _cloud.State == CloudState.Off)
                {
                    return "Облачная база не подключена";
                }

                var text = "Облачная база: " + Settings.Cloud.ServerUrl + "\nПользователь: " + Settings.Cloud.Login;
                text += _cloud.State switch
                {
                    CloudState.Ready => "\nПодключено, контактов: " + _cloud.Contacts.Count,
                    CloudState.Offline => "\nНет связи — показаны контакты из кэша. " + _cloud.LastError,
                    CloudState.NeedsLogin => "\nНужно войти заново. " + _cloud.LastError,
                    CloudState.NeedsPassphrase => string.IsNullOrEmpty(_cloud.LastError)
                        ? "\nНужна парольная фраза."
                        : "\n" + _cloud.LastError,
                    _ => string.Empty,
                };
                if (_cloud.UndecryptableCount > 0)
                {
                    text += "\nНе удалось расшифровать записей: " + _cloud.UndecryptableCount;
                }

                return text;
            }
        }

        /// <summary>Пункт меню: «Подключиться…», «Войти заново…» или «Ввести парольную фразу…».</summary>
        public string CloudConnectHeader => _cloud?.State switch
        {
            CloudState.NeedsLogin => "Войти в облачную базу заново…",
            CloudState.NeedsPassphrase => "Ввести парольную фразу облачной базы…",
            CloudState.Ready or CloudState.Offline => "Подключиться к другой облачной базе…",
            _ => "Подключиться к облачной базе…",
        };

        public string LocalEncryptionHeader =>
            _localEncrypted ? "Снять шифрование локальной базы…" : "Зашифровать локальную базу…";

        public GroupItem SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (Set(ref _selectedGroup, value))
                {
                    if (value != null)
                    {
                        Settings.SelectedGroupId = value.Kind == GroupKind.All ? AllGroupId
                            : value.Kind == GroupKind.Ungrouped ? UngroupedGroupId : 0;
                        Settings.SelectedGroupName = value.Kind == GroupKind.Normal ? value.Name : string.Empty;
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

        // =====================================================================
        // Загрузка и объединение баз
        // =====================================================================

        /// <summary>Открывает (при необходимости создаёт) локальную базу и загружает данные.</summary>
        public void OpenDatabase(string path)
        {
            try
            {
                _database = new Database(path);
                _database.EnsureCreated();
                _localLocked = !UnlockLocalDatabase(askUser: true);
                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось открыть базу данных:\n" + path + "\n\n" + ex.Message, "База данных");
            }

            RaiseSecurityProperties();
        }

        /// <summary>
        /// Если локальная база зашифрована — подбирает ключ: сохранённый на этом компьютере или
        /// из парольной фразы. false — ключа нет (пользователь отказался), локальные контакты скрыты.
        /// </summary>
        private bool UnlockLocalDatabase(bool askUser)
        {
            var info = _database.GetEncryptionInfo();
            _localEncrypted = info != null;
            if (info == null)
            {
                _database.SetKey(null);
                return true;
            }

            var saved = SecretProtector.Unprotect(LocalKeyEntry());
            if (saved != null && VaultCrypto.VerifyCheck(saved, info.Check))
            {
                _database.SetKey(saved);
                return true;
            }

            if (!askUser || _dialogs == null)
            {
                return false;
            }

            var message = "Локальная база зашифрована:\n" + _database.DatabasePath + "\n\nВведите её парольную фразу.";
            while (true)
            {
                var phrase = _dialogs.PromptPassphrase("Локальная база", message, confirm: false);
                if (phrase == null)
                {
                    StatusText = "Локальная база зашифрована, фраза не введена — показаны только облачные контакты (F5 — ввести снова)";
                    return false;
                }

                var key = VaultCrypto.DeriveKey(phrase, info.Salt, info.Iterations);
                if (VaultCrypto.VerifyCheck(key, info.Check))
                {
                    _database.SetKey(key);
                    Settings.LocalKeys[_database.DatabasePath] = SecretProtector.Protect(key);
                    SettingsService.Save(Settings);
                    return true;
                }

                message = "Неверная парольная фраза. Попробуйте ещё раз.";
            }
        }

        private string LocalKeyEntry() =>
            _database != null && Settings.LocalKeys.TryGetValue(_database.DatabasePath, out var value) ? value : null;

        /// <summary>
        /// Перечитывает обе базы и объединяет их. Контакты сливаются с уже показанными
        /// (обновляются на месте, добавляются, удаляются), чтобы таблица не теряла прокрутку и выделение.
        /// </summary>
        public void ReloadAll()
        {
            var previousLabelName = SelectedLabel?.Name;
            var previousContactKey = SelectedContact?.Key;

            var localGroups = new List<GroupItem>();
            var localLabels = new List<LabelItem>();
            var local = new List<Contact>();
            if (_database != null && !_localLocked)
            {
                // Ревизию читаем до данных: если базу изменят во время загрузки, следующая проверка это заметит.
                _knownRevision = _database.GetRevision();
                localGroups = _database.LoadGroups();
                localLabels = _database.LoadLabels();
                local = _database.LoadContacts();
            }

            var cloud = _cloud?.Contacts ?? (IReadOnlyList<Contact>)Array.Empty<Contact>();

            // Дубли: локальная копия с теми же полями, что у облачной, не показывается
            // (даже если у неё другая группа или метки — приоритет у облака).
            var cloudPrints = new HashSet<string>(cloud.Select(ContactMatcher.Fingerprint));
            var visibleLocal = local.Where(c => !cloudPrints.Contains(ContactMatcher.Fingerprint(c))).ToList();

            // Расхождения: тот же контакт (совпал ID или имя) в обеих базах, но данные разные.
            var cloudByKey = new Dictionary<string, List<Contact>>();
            foreach (var contact in cloud)
            {
                foreach (var key in ContactMatcher.MatchKeys(contact))
                {
                    if (!cloudByKey.TryGetValue(key, out var list))
                    {
                        cloudByKey[key] = list = new List<Contact>();
                    }

                    list.Add(contact);
                }
            }

            var conflicted = new HashSet<string>();
            foreach (var contact in visibleLocal)
            {
                foreach (var key in ContactMatcher.MatchKeys(contact))
                {
                    if (cloudByKey.TryGetValue(key, out var matches))
                    {
                        conflicted.Add(contact.Key);
                        foreach (var match in matches)
                        {
                            conflicted.Add(match.Key);
                        }
                    }
                }
            }

            MergeContacts(cloud.Concat(visibleLocal).ToList());
            foreach (var contact in Contacts)
            {
                contact.HasConflict = conflicted.Contains(contact.Key);
            }

            RebuildGroups(localGroups);
            RebuildLabels(localLabels, previousLabelName);
            RestoreSelectedGroup();
            RefreshView();

            if (previousContactKey != null)
            {
                SelectedContact = Contacts.FirstOrDefault(c => c.Key == previousContactKey);
            }

            RaiseSecurityProperties();
        }

        /// <summary>Приводит коллекцию контактов к загруженному списку с минимумом изменений.</summary>
        private void MergeContacts(IList<Contact> loaded)
        {
            var loadedKeys = new HashSet<string>(loaded.Select(c => c.Key));
            for (var i = Contacts.Count - 1; i >= 0; i--)
            {
                if (!loadedKeys.Contains(Contacts[i].Key))
                {
                    Contacts.RemoveAt(i);
                }
            }

            var existing = Contacts.ToDictionary(c => c.Key);
            for (var i = 0; i < loaded.Count; i++)
            {
                var fresh = loaded[i];
                if (!existing.TryGetValue(fresh.Key, out var current))
                {
                    var added = fresh.Clone();
                    Contacts.Insert(i, added);
                    existing[added.Key] = added;
                    continue;
                }

                current.CopyValuesFrom(fresh);
                current.Id = fresh.Id;
                current.GroupId = fresh.GroupId;
                current.LabelIds.Clear();
                current.LabelIds.UnionWith(fresh.LabelIds);
                var index = Contacts.IndexOf(current);
                if (index != i)
                {
                    Contacts.Move(index, i);
                }
            }
        }

        /// <summary>Группы: служебные «Все» и «Без группы», затем объединение имён из обеих баз.</summary>
        private void RebuildGroups(IEnumerable<GroupItem> localGroups)
        {
            var names = new SortedSet<string>(NameComparer);
            foreach (var group in localGroups)
            {
                names.Add(group.Name);
            }

            foreach (var contact in Contacts)
            {
                if (!string.IsNullOrWhiteSpace(contact.GroupName))
                {
                    names.Add(contact.GroupName.Trim());
                }
            }

            Groups.Clear();
            Groups.Add(new GroupItem(AllGroupId, "Все", GroupKind.All) { Count = Contacts.Count });
            Groups.Add(new GroupItem(UngroupedGroupId, "Без группы", GroupKind.Ungrouped)
            {
                Count = Contacts.Count(c => string.IsNullOrWhiteSpace(c.GroupName)),
            });

            var id = 1;
            foreach (var name in names)
            {
                Groups.Add(new GroupItem(id++, name, GroupKind.Normal)
                {
                    Count = Contacts.Count(c => NameComparer.Equals((c.GroupName ?? string.Empty).Trim(), name)),
                });
            }
        }

        private void RebuildLabels(IEnumerable<LabelItem> localLabels, string previousLabelName)
        {
            var names = new SortedSet<string>(NameComparer);
            foreach (var label in localLabels)
            {
                names.Add(label.Name);
            }

            foreach (var contact in Contacts)
            {
                names.UnionWith(contact.LabelNames);
            }

            Labels.Clear();
            var id = 1;
            foreach (var name in names)
            {
                Labels.Add(new LabelItem(id++, name));
            }

            _selectedLabel = previousLabelName == null ? null : Labels.FirstOrDefault(l => NameComparer.Equals(l.Name, previousLabelName));
            OnPropertyChanged(nameof(SelectedLabel));
        }

        private void RestoreSelectedGroup()
        {
            _selectedGroup = Settings.SelectedGroupId == UngroupedGroupId
                ? Groups.FirstOrDefault(g => g.Kind == GroupKind.Ungrouped)
                : Settings.SelectedGroupId == AllGroupId || string.IsNullOrEmpty(Settings.SelectedGroupName)
                    ? null
                    : Groups.FirstOrDefault(g => g.Kind == GroupKind.Normal && NameComparer.Equals(g.Name, Settings.SelectedGroupName));
            _selectedGroup ??= Groups.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedGroup));
        }

        /// <summary>
        /// Раз в несколько секунд сверяет ревизию локальной базы и забирает изменения из облака;
        /// если что-то поменялось на другом компьютере — перечитывает. Запросы идут в фоне.
        /// </summary>
        private async Task CheckForExternalChangesAsync()
        {
            if (_checking || _busy)
            {
                return;
            }

            _checking = true;
            try
            {
                var changed = false;
                var database = _database;
                if (database != null && !_localLocked)
                {
                    try
                    {
                        var revision = await Task.Run(database.GetRevision);
                        if (_databaseUnavailable)
                        {
                            _databaseUnavailable = false;
                            StatusText = "Связь с локальной базой восстановлена";
                        }

                        changed |= database == _database && revision != _knownRevision;
                    }
                    catch (Exception ex)
                    {
                        if (!_databaseUnavailable)
                        {
                            _databaseUnavailable = true;
                            StatusText = "Нет связи с локальной базой, повторяю попытку… (" + ex.Message + ")";
                        }
                    }
                }

                if (_cloud != null)
                {
                    var before = _cloud.State;
                    changed |= await _cloud.PollAsync();
                    if (_cloud.State != before)
                    {
                        RaiseSecurityProperties();
                        if (_cloud.State == CloudState.NeedsLogin || _cloud.State == CloudState.NeedsPassphrase)
                        {
                            StatusText = "Облачная база: " + CloudToolTip.Split('\n').Last();
                        }
                    }
                }

                if (changed)
                {
                    ReloadAll();
                }
            }
            catch (CryptographicException)
            {
                // Локальную базу зашифровали на другом компьютере — нужна фраза.
                _localLocked = !UnlockLocalDatabase(askUser: true);
                ReloadAll();
            }
            catch (Exception ex)
            {
                StatusText = "Не удалось обновить данные: " + ex.Message;
            }
            finally
            {
                _checking = false;
            }
        }

        private async void Refresh()
        {
            try
            {
                if (_database != null && _localLocked)
                {
                    _localLocked = !UnlockLocalDatabase(askUser: true);
                }

                if (_cloud != null)
                {
                    await _cloud.PollAsync();
                }

                ReloadAll();
                StatusText = "Данные обновлены";
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось перечитать данные: " + ex.Message, "База данных");
            }
        }

        private void RaiseSecurityProperties()
        {
            OnPropertyChanged(nameof(CloudStatusText));
            OnPropertyChanged(nameof(CloudToolTip));
            OnPropertyChanged(nameof(CloudConnectHeader));
            OnPropertyChanged(nameof(LocalEncryptionHeader));
            CommandManager.InvalidateRequerySuggested();
        }

        // =====================================================================
        // Подключение к программам
        // =====================================================================

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

        // =====================================================================
        // Фильтры
        // =====================================================================

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
                var group = (contact.GroupName ?? string.Empty).Trim();
                if (SelectedGroup.Kind == GroupKind.Ungrouped && group.Length > 0)
                {
                    return false;
                }

                if (SelectedGroup.Kind == GroupKind.Normal && !NameComparer.Equals(group, SelectedGroup.Name))
                {
                    return false;
                }
            }

            // Фильтр по метке (только если панель меток включена). Работает независимо от групп.
            if (ShowLabelsPanel && SelectedLabel != null && !contact.LabelNames.Contains(SelectedLabel.Name))
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

        // =====================================================================
        // Метки и группы (по имени — в обеих базах)
        // =====================================================================

        private void AddLabel()
        {
            var name = _dialogs?.PromptText("Новая метка", "Название метки:", string.Empty)?.Trim();
            if (string.IsNullOrEmpty(name) || !EnsureLocalWritable())
            {
                return;
            }

            try
            {
                _database.EnsureLabel(name);
                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось создать метку: " + ex.Message, "Метки");
            }
        }

        private void EditLabel()
        {
            var label = SelectedLabel;
            var name = label == null ? null : _dialogs?.PromptText("Переименовать метку", "Название метки:", label.Name)?.Trim();
            if (string.IsNullOrEmpty(name) || name == label.Name)
            {
                return;
            }

            RenameOrDelete(
                "Метки",
                local: () => FindLocal(_database?.LoadLabels(), label.Name, l => l.Name, l => l.Id) is long id ? (Action)(() => _database.RenameLabel(id, name)) : null,
                cloudChange: contact =>
                {
                    if (!contact.LabelNames.Remove(label.Name))
                    {
                        return false;
                    }

                    contact.LabelNames.Add(name);
                    return true;
                });
            SelectedLabel = Labels.FirstOrDefault(l => NameComparer.Equals(l.Name, name));
        }

        private void DeleteLabel()
        {
            var label = SelectedLabel;
            if (label == null || _dialogs == null ||
                !_dialogs.Confirm("Удалить метку «" + label.Name + "»?\nМетка снимется с контактов в обеих базах.", "Метки"))
            {
                return;
            }

            SelectedLabel = null;
            RenameOrDelete(
                "Метки",
                local: () => FindLocal(_database?.LoadLabels(), label.Name, l => l.Name, l => l.Id) is long id ? (Action)(() => _database.DeleteLabel(id)) : null,
                cloudChange: contact => contact.LabelNames.Remove(label.Name));
        }

        private void AddGroup()
        {
            var name = _dialogs?.PromptText("Новая группа", "Название группы:", string.Empty)?.Trim();
            if (string.IsNullOrEmpty(name) || !EnsureLocalWritable())
            {
                return;
            }

            try
            {
                _database.EnsureGroup(name);
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

            var name = _dialogs?.PromptText("Переименовать группу", "Название группы:", group.Name)?.Trim();
            if (string.IsNullOrEmpty(name) || name == group.Name)
            {
                return;
            }

            Settings.SelectedGroupName = name;
            RenameOrDelete(
                "Группы",
                local: () => FindLocal(_database?.LoadGroups(), group.Name, g => g.Name, g => g.Id) is long id ? (Action)(() => _database.RenameGroup(id, name)) : null,
                cloudChange: contact =>
                {
                    if (!NameComparer.Equals((contact.GroupName ?? string.Empty).Trim(), group.Name))
                    {
                        return false;
                    }

                    contact.GroupName = name;
                    return true;
                });
        }

        private void DeleteGroup()
        {
            var group = SelectedGroup;
            if (group == null || group.IsSystem || _dialogs == null)
            {
                return;
            }

            if (!_dialogs.Confirm(
                    "Удалить группу «" + group.Name + "»?\nКонтакты группы в обеих базах останутся, но станут «Без группы».",
                    "Группы"))
            {
                return;
            }

            Settings.SelectedGroupId = AllGroupId;
            Settings.SelectedGroupName = string.Empty;
            RenameOrDelete(
                "Группы",
                local: () => FindLocal(_database?.LoadGroups(), group.Name, g => g.Name, g => g.Id) is long id ? (Action)(() => _database.DeleteGroup(id)) : null,
                cloudChange: contact =>
                {
                    if (!NameComparer.Equals((contact.GroupName ?? string.Empty).Trim(), group.Name))
                    {
                        return false;
                    }

                    contact.GroupName = string.Empty;
                    return true;
                });
        }

        private static long? FindLocal<T>(IEnumerable<T> items, string name, Func<T, string> getName, Func<T, long> getId)
        {
            var match = (items ?? Enumerable.Empty<T>()).FirstOrDefault(i => NameComparer.Equals(getName(i), name));
            return match == null ? null : getId(match);
        }

        /// <summary>
        /// Переименование/удаление группы или метки: в локальной базе — одной строкой,
        /// в облаке — правкой каждого контакта, где она встречается (там группы и метки
        /// хранятся внутри зашифрованных контактов).
        /// </summary>
        private async void RenameOrDelete(string title, Func<Action> local, Func<Contact, bool> cloudChange)
        {
            _busy = true;
            try
            {
                if (_database != null && !_localLocked)
                {
                    local()?.Invoke();
                }

                var cloudContacts = _cloud?.Contacts.Select(c => c.Clone()).Where(cloudChange).ToList() ?? new List<Contact>();
                if (cloudContacts.Count > 0)
                {
                    if (!_cloud.CanWrite)
                    {
                        _dialogs?.Error(
                            "Изменение затрагивает облачные контакты (" + cloudContacts.Count + "), но облачная база сейчас недоступна.\n" +
                            "Локальная база изменена; облачные контакты остались как были.", title);
                    }
                    else
                    {
                        var failed = 0;
                        foreach (var contact in cloudContacts)
                        {
                            var result = await _cloud.SaveAsync(contact, force: true);
                            if (result.Result != SaveResult.Saved)
                            {
                                failed++;
                            }
                        }

                        if (failed > 0)
                        {
                            _dialogs?.Error("Не удалось изменить облачных контактов: " + failed + " (их удалили на другом компьютере).", title);
                        }
                    }
                }

                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось выполнить операцию: " + ex.Message, title);
                ReloadAll();
            }
            finally
            {
                _busy = false;
            }
        }

        // =====================================================================
        // Контакты
        // =====================================================================

        private IEnumerable<string> GroupNames() => Groups.Where(g => !g.IsSystem).Select(g => g.Name).ToList();

        private IEnumerable<string> LabelNames() => Labels.Select(l => l.Name).ToList();

        private bool EnsureLocalWritable()
        {
            if (_database != null && !_localLocked)
            {
                return true;
            }

            _dialogs?.Error("Локальная база недоступна: она зашифрована, а парольная фраза не введена (F5 — ввести).", "Локальная база");
            return false;
        }

        private async void AddContact()
        {
            if (_dialogs == null)
            {
                return;
            }

            var contact = new Contact();
            if (SelectedGroup != null && SelectedGroup.Kind == GroupKind.Normal)
            {
                contact.GroupName = SelectedGroup.Name;
            }

            var options = new ContactEditOptions
            {
                ShowCloudChoice = Settings.Cloud.IsConfigured,
                CloudAvailable = _cloud != null && _cloud.CanWrite,
                CloudUnavailableReason = _cloud == null || _cloud.CanWrite ? null : "облачная база сейчас недоступна",
                SaveToCloud = Settings.Cloud.AddToCloudByDefault && _cloud != null && _cloud.CanWrite,
            };
            // При ошибке сохранения (например, нет связи с сервером) окно открывается снова
            // с введёнными данными, чтобы правки не пропали.
            while (_dialogs.EditContact(contact, GroupNames(), LabelNames(), "Новый контакт", options))
            {
                if (options.ShowCloudChoice && options.CloudAvailable)
                {
                    Settings.Cloud.AddToCloudByDefault = options.SaveToCloud;
                    SettingsService.Save(Settings);
                }

                _busy = true;
                try
                {
                    if (options.SaveToCloud)
                    {
                        await _cloud.SaveAsync(contact);
                    }
                    else
                    {
                        if (!EnsureLocalWritable())
                        {
                            return;
                        }

                        _database.ResolveNames(contact);
                        _database.InsertContact(contact);
                        contact.Source = ContactSource.Local;
                    }

                    ReloadAll();
                    SelectedContact = Contacts.FirstOrDefault(c => c.Key == contact.Key);
                    return;
                }
                catch (Exception ex)
                {
                    if (!_dialogs.Confirm(SaveFailedMessage(ex), "Контакты"))
                    {
                        return;
                    }
                }
                finally
                {
                    _busy = false;
                }
            }
        }

        private static string SaveFailedMessage(Exception ex) =>
            "Не удалось сохранить контакт: " + ex.Message + "\n\n" +
            "Открыть его снова, чтобы повторить попытку? Введённые данные сохранятся в окне.";

        private async void EditContact()
        {
            var contact = SelectedContact;
            if (contact == null || _dialogs == null)
            {
                return;
            }

            var draft = contact.Clone();
            var options = new ContactEditOptions
            {
                SourceText = contact.IsCloud ? "Облачная база" : "Локальная база",
            };
            while (_dialogs.EditContact(draft, GroupNames(), LabelNames(), "Изменить контакт", options))
            {
                _busy = true;
                try
                {
                    if (contact.IsCloud)
                    {
                        await SaveCloudEdit(draft);
                    }
                    else if (EnsureLocalWritable())
                    {
                        SaveLocalEdit(draft);
                    }

                    ReloadAll();
                    SelectedContact = Contacts.FirstOrDefault(c => c.Key == draft.Key);
                    return;
                }
                catch (Exception ex)
                {
                    if (!_dialogs.Confirm(SaveFailedMessage(ex), "Контакты"))
                    {
                        ReloadAll();
                        return;
                    }
                }
                finally
                {
                    _busy = false;
                }
            }
        }

        /// <summary>Сохранение в локальную базу; версия строки проверяет, не менял ли её кто-то ещё.</summary>
        private void SaveLocalEdit(Contact draft)
        {
            _database.ResolveNames(draft);
            var result = _database.UpdateContact(draft, draft.Version);
            if (result == SaveResult.Conflict && _dialogs.Confirm(ChangedElsewhereMessage(draft), "Контакты"))
            {
                _database.UpdateContact(draft);
            }
            else if (result == SaveResult.NotFound && _dialogs.Confirm(DeletedElsewhereMessage(draft), "Контакты"))
            {
                _database.InsertContact(draft);
            }
        }

        /// <summary>Сохранение в облако; сервер проверяет версию записи.</summary>
        private async Task SaveCloudEdit(Contact draft)
        {
            var result = await _cloud.SaveAsync(draft);
            if (result.Result == SaveResult.Conflict && _dialogs.Confirm(ChangedElsewhereMessage(draft), "Контакты"))
            {
                await _cloud.SaveAsync(draft, force: true);
            }
            else if (result.Result == SaveResult.NotFound && _dialogs.Confirm(DeletedElsewhereMessage(draft), "Контакты"))
            {
                draft.CloudId = string.Empty;
                await _cloud.SaveAsync(draft);
            }
        }

        private static string ChangedElsewhereMessage(Contact contact) =>
            "Контакт «" + DisplayName(contact) + "» изменили на другом компьютере, пока он был открыт у вас.\n\n" +
            "Да — сохранить ваш вариант поверх.\nНет — отменить ваши правки и показать актуальные данные.";

        private static string DeletedElsewhereMessage(Contact contact) =>
            "Контакт «" + DisplayName(contact) + "» удалили на другом компьютере.\n\nСохранить ваш вариант как новый контакт?";

        private static string DisplayName(Contact contact) =>
            string.IsNullOrWhiteSpace(contact.Name) ? "(без имени)" : contact.Name;

        private async void DeleteContact()
        {
            var contact = SelectedContact;
            if (contact == null || _dialogs == null)
            {
                return;
            }

            var where = contact.IsCloud ? " из облачной базы" : " из локальной базы";
            if (!_dialogs.Confirm("Удалить контакт «" + DisplayName(contact) + "»" + where + "?", "Контакты"))
            {
                return;
            }

            _busy = true;
            try
            {
                if (contact.IsCloud)
                {
                    if (!await _cloud.DeleteAsync(contact) &&
                        _dialogs.Confirm("Контакт изменили на другом компьютере. Всё равно удалить?", "Контакты"))
                    {
                        await _cloud.DeleteAsync(contact, force: true);
                    }
                }
                else if (EnsureLocalWritable())
                {
                    // Если контакт уже удалили на другом компьютере — результат тот же, просто перечитываем.
                    _database.DeleteContact(contact.Id);
                }

                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось удалить контакт: " + ex.Message, "Контакты");
            }
            finally
            {
                _busy = false;
            }
        }

        // =====================================================================
        // Синхронизация облака и локальной базы
        // =====================================================================

        /// <summary>Копирует один контакт в другую базу (из контекстного меню).</summary>
        private async void CopyToOther(Contact contact)
        {
            if (contact == null)
            {
                return;
            }

            _busy = true;
            try
            {
                var report = await CopyContacts(new[] { contact }, toCloud: !contact.IsCloud);
                if (report != null)
                {
                    StatusText = report.ToStatus(toCloud: !contact.IsCloud);
                }

                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs?.Error("Не удалось скопировать контакт: " + ex.Message, "Синхронизация");
                ReloadAll();
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Копирует все контакты одной базы в другую, спрашивая при расхождениях.</summary>
        private async void Synchronize(bool toCloud)
        {
            if (_dialogs == null || _cloud == null || _database == null)
            {
                return;
            }

            var direction = toCloud ? "из локальной базы в облачную" : "из облачной базы в локальную";
            if (!_dialogs.Confirm(
                    "Скопировать контакты " + direction + "?\n\n" +
                    "• новые контакты будут добавлены;\n" +
                    "• дубли (все поля совпадают, группа и метки не сравниваются) пропускаются;\n" +
                    "• если контакт есть в обеих базах с разными данными, программа спросит, что делать.\n\n" +
                    "Удаления не переносятся: контакт, удалённый в одной базе, в другой останется.",
                    "Синхронизация"))
            {
                return;
            }

            _busy = true;
            SyncReport report = null;
            try
            {
                await _cloud.PollAsync();
                var sources = toCloud ? _database.LoadContacts() : _cloud.Contacts.ToList();
                report = await CopyContacts(sources, toCloud);
                ReloadAll();
            }
            catch (Exception ex)
            {
                _busy = false;
                _dialogs.Error("Синхронизация прервана: " + ex.Message, "Синхронизация");
                ReloadAll();
            }
            finally
            {
                _busy = false;
            }

            // Отчёт — после снятия блокировки: пока окно открыто, изменения коллег продолжают подтягиваться.
            if (report != null)
            {
                StatusText = (toCloud ? "Скопировано в облако: " : "Скопировано в локальную базу: ") +
                             report.Added + ", заменено: " + report.Replaced;
                _dialogs.Info(report.ToText(toCloud), "Синхронизация");
            }
        }

        /// <summary>
        /// Переносит контакты в другую базу. Дубли (совпали все поля) пропускаются; при расхождении
        /// (тот же контакт, другие данные) — вопрос: заменить, оставить, сохранить оба.
        /// null — пользователь отменил.
        /// </summary>
        private async Task<SyncReport> CopyContacts(IEnumerable<Contact> sources, bool toCloud)
        {
            if (toCloud && !_cloud.CanWrite)
            {
                throw new CloudException(_cloud.State == CloudState.Offline ? "Нет связи с облачной базой." : "Облачная база не подключена.");
            }

            if (!toCloud && !EnsureLocalWritable())
            {
                return null;
            }

            var report = new SyncReport();
            ConflictDecision remembered = null;
            var targets = toCloud ? _cloud.Contacts.ToList() : _database.LoadContacts();
            var sourceTitle = toCloud ? "Локальная база" : "Облачная база";
            var targetTitle = toCloud ? "Облачная база" : "Локальная база";

            foreach (var source in sources)
            {
                var print = ContactMatcher.Fingerprint(source);
                if (targets.Any(t => ContactMatcher.Fingerprint(t) == print))
                {
                    report.Unchanged++;
                    continue;
                }

                var match = targets.FirstOrDefault(t => ContactMatcher.IsSameContact(t, source));
                if (match == null)
                {
                    await WriteToTarget(source, null, toCloud);
                    report.Added++;
                    continue;
                }

                var decision = remembered ?? _dialogs.ResolveConflict(new ConflictRequest
                {
                    Title = "Расхождение при синхронизации",
                    Message = "Контакт «" + DisplayName(source) + "» есть в обеих базах, но данные различаются.",
                    LeftTitle = sourceTitle,
                    Left = source,
                    RightTitle = targetTitle,
                    Right = match,
                    LeftButton = "Заменить данными слева",
                    RightButton = "Оставить как в «" + targetTitle + "»",
                    BothButton = "Сохранить оба",
                    ShowApplyToAll = true,
                });
                if (decision.ApplyToAll)
                {
                    remembered = decision;
                }

                switch (decision.Choice)
                {
                    case ConflictChoice.Left:
                        await WriteToTarget(source, match, toCloud);
                        report.Replaced++;
                        break;
                    case ConflictChoice.Both:
                        await WriteToTarget(source, null, toCloud);
                        report.Added++;
                        break;
                    case ConflictChoice.Right:
                        report.Skipped++;
                        break;
                    default:
                        report.Cancelled = true;
                        return report;
                }
            }

            return report;
        }

        /// <summary>Записывает данные source в другую базу: новым контактом или поверх target.</summary>
        private async Task WriteToTarget(Contact source, Contact target, bool toCloud)
        {
            var copy = source.Clone();
            if (toCloud)
            {
                copy.Source = ContactSource.Cloud;
                copy.CloudId = target?.CloudId ?? string.Empty;
                copy.Version = target?.Version ?? 0;
                await _cloud.SaveAsync(copy, force: true);
                return;
            }

            copy.Source = ContactSource.Local;
            _database.ResolveNames(copy);
            if (target == null)
            {
                _database.InsertContact(copy);
            }
            else
            {
                copy.Id = target.Id;
                if (_database.UpdateContact(copy) == SaveResult.NotFound)
                {
                    _database.InsertContact(copy);
                }
            }
        }

        /// <summary>Разбор расхождения у выбранного контакта: какую версию оставить.</summary>
        private async void ResolveConflict(Contact contact)
        {
            if (contact == null || _dialogs == null)
            {
                return;
            }

            var cloud = contact.IsCloud ? contact : _cloud?.Contacts.FirstOrDefault(c => ContactMatcher.IsSameContact(c, contact));
            var local = !contact.IsCloud ? contact
                : _database == null || _localLocked ? null
                : _database.LoadContacts().FirstOrDefault(c => ContactMatcher.IsSameContact(c, contact));
            if (cloud == null || local == null)
            {
                ReloadAll();
                return;
            }

            var decision = _dialogs.ResolveConflict(new ConflictRequest
            {
                Title = "Расхождение между базами",
                Message = "Контакт «" + DisplayName(contact) + "» есть в обеих базах с разными данными. Какую версию оставить?",
                LeftTitle = "Облачная база",
                Left = cloud,
                RightTitle = "Локальная база",
                Right = local,
                LeftButton = "Оставить облачную",
                RightButton = "Оставить локальную",
                BothButton = "Оставить обе как есть",
            });

            _busy = true;
            try
            {
                switch (decision.Choice)
                {
                    case ConflictChoice.Left:
                        await WriteToTarget(cloud, local, toCloud: false);
                        StatusText = "Локальная копия обновлена из облака";
                        break;
                    case ConflictChoice.Right:
                        if (!_cloud.CanWrite)
                        {
                            throw new CloudException("Облачная база сейчас недоступна.");
                        }

                        await WriteToTarget(local, cloud, toCloud: true);
                        StatusText = "Облачная версия обновлена из локальной базы";
                        break;
                }

                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось применить выбор: " + ex.Message, "Расхождение");
                ReloadAll();
            }
            finally
            {
                _busy = false;
            }
        }

        private class SyncReport
        {
            public int Added { get; set; }

            public int Replaced { get; set; }

            public int Skipped { get; set; }

            public int Unchanged { get; set; }

            public bool Cancelled { get; set; }

            public string ToText(bool toCloud) =>
                (Cancelled ? "Синхронизация остановлена.\n\n" : "Синхронизация завершена.\n\n") +
                "Добавлено " + (toCloud ? "в облако" : "в локальную базу") + ": " + Added + "\n" +
                "Заменено: " + Replaced + "\n" +
                "Оставлено без изменений по вашему выбору: " + Skipped + "\n" +
                "Уже совпадали: " + Unchanged;

            public string ToStatus(bool toCloud) =>
                Added + Replaced > 0
                    ? (toCloud ? "Контакт скопирован в облако" : "Контакт скопирован в локальную базу")
                    : Unchanged > 0 ? "Такой контакт там уже есть" : "Копирование отменено";
        }

        // =====================================================================
        // Облако: подключение и отключение
        // =====================================================================

        private async void ConnectCloud()
        {
            if (_dialogs == null)
            {
                return;
            }

            try
            {
                var cloud = Settings.Cloud;
                var token = SecretProtector.UnprotectString(cloud.ProtectedToken);
                CloudVault vault = null;

                // Если вход ещё действует (нужна только фраза) — не спрашиваем пароль заново.
                if (_cloud?.State == CloudState.NeedsPassphrase && !string.IsNullOrEmpty(token))
                {
                    try
                    {
                        using var client = new CloudClient(cloud.ServerUrl, token);
                        vault = await client.GetVaultAsync();
                    }
                    catch (CloudAuthException)
                    {
                        vault = null;
                    }
                }

                if (vault == null)
                {
                    var login = _dialogs.LoginToCloud(cloud.ServerUrl, cloud.Login);
                    if (login == null)
                    {
                        return;
                    }

                    CloudStore.StoreLogin(cloud, login.ServerUrl, login.Login);
                    token = login.Login.Token;
                    using var client = new CloudClient(cloud.ServerUrl, token);
                    vault = await client.GetVaultAsync();
                }

                var key = await ObtainCloudKey(vault, token);
                if (key == null)
                {
                    SettingsService.Save(Settings);
                    _cloud = new CloudStore(Settings.Cloud);
                    RaiseSecurityProperties();
                    return;
                }

                CloudStore.StoreKey(cloud, key);
                SettingsService.Save(Settings);

                // Ключ получен для хранилища, которое на сервере сейчас: кэш перечитываем с нуля.
                CloudStore.DeleteCache(cloud);
                _cloud = new CloudStore(Settings.Cloud);
                await _cloud.PollAsync();
                ReloadAll();
                StatusText = _cloud.State == CloudState.Ready
                    ? "Облачная база подключена: контактов " + _cloud.Contacts.Count
                    : "Облачная база: " + _cloud.LastError;
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось подключиться к облачной базе:\n" + ex.Message, "Облачная база");
            }
        }

        /// <summary>
        /// Ключ облачной базы из парольной фразы. Если хранилище новое, фразу задаёт администратор.
        /// null — отмена.
        /// </summary>
        private async Task<byte[]> ObtainCloudKey(CloudVault vault, string token)
        {
            if (!vault.Initialized)
            {
                if (!Settings.Cloud.IsAdmin)
                {
                    _dialogs.Error(
                        "Облачная база ещё не настроена: первым должен войти администратор сервера и задать парольную фразу.",
                        "Облачная база");
                    return null;
                }

                var phrase = _dialogs.PromptPassphrase(
                    "Новая облачная база",
                    "Придумайте парольную фразу команды. Ею шифруются все контакты облачной базы: сервер её не знает.\n\n" +
                    "Сообщите фразу коллегам надёжным способом. Если фразу потерять, контакты облачной базы восстановить будет невозможно.",
                    confirm: true);
                if (phrase == null)
                {
                    return null;
                }

                var salt = VaultCrypto.NewSalt();
                var key = await Task.Run(() => VaultCrypto.DeriveKey(phrase, salt, VaultCrypto.DefaultIterations));
                using var client = new CloudClient(Settings.Cloud.ServerUrl, token);
                await client.InitVaultAsync(salt, VaultCrypto.DefaultIterations, VaultCrypto.CreateCheck(key));
                return key;
            }

            var vaultSalt = Convert.FromBase64String(vault.Salt);
            var check = Convert.FromBase64String(vault.Check);

            // Повторный вход того же пользователя (истёк токен, сменили пароль): сохранённый ключ
            // ещё подходит к хранилищу — фразу не спрашиваем.
            var storedKey = SecretProtector.Unprotect(Settings.Cloud.ProtectedKey);
            if (storedKey != null && VaultCrypto.VerifyCheck(storedKey, check))
            {
                return storedKey;
            }

            var message = "Введите парольную фразу облачной базы (её задал администратор).";
            while (true)
            {
                var phrase = _dialogs.PromptPassphrase("Облачная база", message, confirm: false);
                if (phrase == null)
                {
                    return null;
                }

                var key = await Task.Run(() => VaultCrypto.DeriveKey(phrase, vaultSalt, vault.Iterations));
                if (VaultCrypto.VerifyCheck(key, check))
                {
                    return key;
                }

                message = "Неверная парольная фраза. Попробуйте ещё раз.";
            }
        }

        private async void DisconnectCloud()
        {
            if (_dialogs == null || !_dialogs.Confirm(
                    "Отключиться от облачной базы?\n\nОблачные контакты пропадут из списка на этом компьютере " +
                    "(на сервере они останутся). Локальная база не изменится.",
                    "Облачная база"))
            {
                return;
            }

            await CloudStore.DisconnectAsync(Settings.Cloud);
            SettingsService.Save(Settings);
            _cloud = new CloudStore(Settings.Cloud);
            ReloadAll();
            StatusText = "Облачная база отключена";
        }

        // =====================================================================
        // Шифрование локальной базы
        // =====================================================================

        private async void ToggleLocalEncryption()
        {
            if (_dialogs == null || _database == null)
            {
                return;
            }

            try
            {
                if (_database.GetEncryptionInfo() == null)
                {
                    var phrase = _dialogs.PromptPassphrase(
                        "Шифрование локальной базы",
                        "Поля контактов (имя, комментарий, ID, логины и пароли) будут зашифрованы парольной фразой.\n\n" +
                        "• На других компьютерах, работающих с этой базой, фразу нужно будет ввести один раз.\n" +
                        "• Версии программы до 1.1 зашифрованные контакты прочитать не смогут.\n" +
                        "• Если фразу потерять, контакты восстановить будет невозможно.",
                        confirm: true);
                    if (phrase == null)
                    {
                        return;
                    }

                    _busy = true;
                    var salt = VaultCrypto.NewSalt();
                    var key = await Task.Run(() => VaultCrypto.DeriveKey(phrase, salt, VaultCrypto.DefaultIterations));
                    await Task.Run(() => _database.EnableEncryption(key, salt, VaultCrypto.DefaultIterations));
                    Settings.LocalKeys[_database.DatabasePath] = SecretProtector.Protect(key);
                    SettingsService.Save(Settings);
                    StatusText = "Локальная база зашифрована";
                }
                else
                {
                    if (_localLocked)
                    {
                        // Снять шифрование можно, только зная фразу.
                        _localLocked = !UnlockLocalDatabase(askUser: true);
                        if (_localLocked)
                        {
                            return;
                        }
                    }

                    if (!_dialogs.Confirm(
                            "Снять шифрование локальной базы?\n\nКонтакты будут храниться в открытом виде, " +
                            "как в версиях программы до 1.1.", "Шифрование локальной базы"))
                    {
                        return;
                    }

                    _busy = true;
                    await Task.Run(_database.DisableEncryption);
                    Settings.LocalKeys.Remove(_database.DatabasePath);
                    SettingsService.Save(Settings);
                    StatusText = "Шифрование локальной базы снято";
                }

                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось изменить шифрование: " + ex.Message, "Шифрование локальной базы");
            }
            finally
            {
                _busy = false;
                RaiseSecurityProperties();
            }
        }

        // =====================================================================
        // Настройки и импорт
        // =====================================================================

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
            if (_dialogs == null || !EnsureLocalWritable())
            {
                return;
            }

            // Дубликаты ищутся по списку в памяти — сначала подтягиваем то, что добавили коллеги.
            try
            {
                ReloadAll();
            }
            catch (Exception ex)
            {
                _dialogs.Error("Не удалось перечитать базу данных: " + ex.Message, "Импорт");
                return;
            }

            // Импорт идёт в локальную базу: группы — её собственные (с настоящими id).
            if (_dialogs.ImportContacts(_database, Contacts.ToList(), _database.LoadGroups()))
            {
                ReloadAll();
            }
        }
    }
}
