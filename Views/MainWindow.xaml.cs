using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RemoteAccessAddressBook.Models;
using RemoteAccessAddressBook.Services;
using RemoteAccessAddressBook.ViewModels;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>Главное окно приложения.</summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly AppSettings _settings;
        private Dictionary<DataGridColumn, string> _toolColumns;
        private Dictionary<string, DataGridColumn[]> _columnGroups;
        private DataGridColumn _contextMenuColumn;

        public MainWindow()
        {
            InitializeComponent();

            var firstRun = !SettingsService.SettingsFileExists;
            _settings = SettingsService.Load();

            if (firstRun)
            {
                // Первый запуск: подставляем пути к установленным программам.
                ToolDiscoveryService.FillMissing(_settings);
                SettingsService.Save(_settings);
            }

            ThemeService.Apply(_settings.Theme);
            _viewModel = new MainViewModel(_settings, new DialogService(this));
            DataContext = _viewModel;

            _toolColumns = new Dictionary<DataGridColumn, string>
            {
                { AnyDeskColumn, ToolKeys.AnyDesk },
                { RudesktopColumn, ToolKeys.Rudesktop },
                { AssistantColumn, ToolKeys.Assistant },
                { AmmyyColumn, ToolKeys.Ammyy },
                { RdpColumn, ToolKeys.Rdp },
            };

            // Колонки «Пароль» и «RDP Пароль» существуют парами (маска и открытый текст),
            // ширина у пары общая.
            _columnGroups = new Dictionary<string, DataGridColumn[]>
            {
                { "Source", new DataGridColumn[] { SourceColumn } },
                { "Name", new DataGridColumn[] { NameColumn } },
                { "Comment", new DataGridColumn[] { CommentColumn } },
                { "AnyDesk", new DataGridColumn[] { AnyDeskColumn } },
                { "Rudesktop", new DataGridColumn[] { RudesktopColumn } },
                { "Assistant", new DataGridColumn[] { AssistantColumn } },
                { "Ammyy", new DataGridColumn[] { AmmyyColumn } },
                { "Password", new DataGridColumn[] { PasswordMaskedColumn, PasswordPlainColumn } },
                { "Rdp", new DataGridColumn[] { RdpColumn } },
                { "RdpLogin", new DataGridColumn[] { RdpLoginColumn } },
                { "RdpPassword", new DataGridColumn[] { RdpPasswordMaskedColumn, RdpPasswordPlainColumn } },
            };

            RestoreWindowPlacement();
            RestoreColumnWidths();
            RestoreColumnOrder();
            UpdatePasswordColumns();
            ContactsGrid.ColumnReordered += OnColumnReordered;
            StateChanged += OnStateChanged;
            Closing += OnClosing;
        }

        private void RestoreWindowPlacement()
        {
            if (_settings.WindowWidth > 100 && _settings.WindowHeight > 100)
            {
                Width = _settings.WindowWidth;
                Height = _settings.WindowHeight;
            }

            if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
            {
                var virtualLeft = SystemParameters.VirtualScreenLeft;
                var virtualTop = SystemParameters.VirtualScreenTop;
                var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
                var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

                if (_settings.WindowLeft >= virtualLeft && _settings.WindowLeft < virtualRight - 100 &&
                    _settings.WindowTop >= virtualTop && _settings.WindowTop < virtualBottom - 100)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = _settings.WindowLeft;
                    Top = _settings.WindowTop;
                }
            }

            if (_settings.WindowMaximized)
            {
                WindowState = WindowState.Maximized;
            }

            ApplyMaximizedMargin();
        }

        /// <summary>Восстанавливает ширину колонок из настроек.</summary>
        private void RestoreColumnWidths()
        {
            if (_settings.ColumnWidths == null)
            {
                return;
            }

            foreach (var group in _columnGroups)
            {
                if (!_settings.ColumnWidths.TryGetValue(group.Key, out var width) ||
                    double.IsNaN(width) || width < 20 || width > 2000)
                {
                    continue;
                }

                foreach (var column in group.Value)
                {
                    column.Width = new DataGridLength(width);
                }
            }
        }

        /// <summary>Запоминает ширину колонок, которую выставил пользователь.</summary>
        private void SaveColumnWidths()
        {
            _settings.ColumnWidths ??= new Dictionary<string, double>();
            foreach (var group in _columnGroups)
            {
                // У пары колонок берём ширину видимой.
                foreach (var column in group.Value)
                {
                    if (column.Visibility != Visibility.Visible)
                    {
                        continue;
                    }

                    var width = column.ActualWidth;
                    if (width >= 20)
                    {
                        _settings.ColumnWidths[group.Key] = Math.Round(width);
                    }

                    break;
                }
            }
        }

        /// <summary>Восстанавливает порядок колонок из настроек.</summary>
        private void RestoreColumnOrder()
        {
            if (_settings.ColumnOrder == null || _settings.ColumnOrder.Count == 0)
            {
                return;
            }

            // Колонки в сохранённом порядке; тех, которых в нём нет (например, появились
            // в новой версии), вставляем на их место по умолчанию.
            var order = _settings.ColumnOrder.Where(_columnGroups.ContainsKey).Distinct().ToList();
            var defaults = _columnGroups.Keys.ToList();
            foreach (var key in defaults.Where(key => !order.Contains(key)).ToList())
            {
                order.Insert(Math.Min(defaults.IndexOf(key), order.Count), key);
            }

            ApplyColumnOrder(order);
        }

        /// <summary>Текущий порядок колонок слева направо (пара «маска/открытый текст» — одна колонка).</summary>
        private List<string> CurrentColumnOrder()
        {
            // У пары берём положение видимой колонки: её и двигал пользователь.
            return _columnGroups
                .OrderBy(group => (group.Value.FirstOrDefault(c => c.Visibility == Visibility.Visible) ?? group.Value[0]).DisplayIndex)
                .Select(group => group.Key)
                .ToList();
        }

        /// <summary>Расставляет колонки по порядку; колонки пары всегда стоят рядом.</summary>
        private void ApplyColumnOrder(IEnumerable<string> order)
        {
            var index = 0;
            foreach (var key in order)
            {
                foreach (var column in _columnGroups[key])
                {
                    column.DisplayIndex = index++;
                }
            }
        }

        /// <summary>
        /// После перетаскивания колонки подтягиваем к ней скрытую половину пары, иначе
        /// «Показать пароли» вывел бы колонку пароля на старом месте.
        /// </summary>
        private void OnColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyColumnOrder(CurrentColumnOrder())));
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveColumnWidths();
            _settings.ColumnOrder = CurrentColumnOrder();

            _settings.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
            }
            else
            {
                _settings.WindowLeft = RestoreBounds.Left;
                _settings.WindowTop = RestoreBounds.Top;
                _settings.WindowWidth = RestoreBounds.Width;
                _settings.WindowHeight = RestoreBounds.Height;
            }

            SettingsService.Save(_settings);
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            ApplyMaximizedMargin();
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Восстановить" : "Развернуть";
        }

        /// <summary>Компенсирует «вылет» окна за края экрана при разворачивании с WindowChrome.</summary>
        private void ApplyMaximizedMargin()
        {
            RootBorder.Margin = WindowState == WindowState.Maximized
                ? new Thickness(7)
                : new Thickness(0);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>Кнопка облака открывает своё меню по обычному клику.</summary>
        private void CloudButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = CloudButton.ContextMenu;
            menu.PlacementTarget = CloudButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void ShowPasswords_Changed(object sender, RoutedEventArgs e) => UpdatePasswordColumns();

        private void UpdatePasswordColumns()
        {
            var show = _viewModel != null && _viewModel.ShowPasswords;
            PasswordPlainColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PasswordMaskedColumn.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            RdpPasswordPlainColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            RdpPasswordMaskedColumn.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ContactsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null)
            {
                return;
            }

            if (cell.DataContext is not Contact contact)
            {
                return;
            }

            _viewModel.SelectedContact = contact;

            if (_toolColumns.TryGetValue(cell.Column, out var toolKey))
            {
                // Двойной клик по ID-полю запускает подключение.
                _viewModel.Connect(toolKey, contact);
                e.Handled = true;
                return;
            }

            // По остальным колонкам — открываем окно редактирования.
            if (_viewModel.EditContactCommand.CanExecute(null))
            {
                _viewModel.EditContactCommand.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Правый клик выделяет строку под курсором (DataGrid сам этого не делает)
        /// и запоминает колонку — от неё зависит, какая программа в меню будет основной.
        /// </summary>
        private void ContactsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _contextMenuColumn = null;

            var cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell?.DataContext is not Contact contact)
            {
                return;
            }

            _contextMenuColumn = cell.Column;
            _viewModel.SelectedContact = contact;
            cell.Focus();
        }

        /// <summary>Заполняет подменю «Подключиться» программами, для которых у контакта задан ID.</summary>
        private void ContactsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var contact = _viewModel.SelectedContact;
            var cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
            var fromKeyboard = e.CursorLeft < 0 && e.CursorTop < 0;
            if (contact == null || (cell == null && !fromKeyboard))
            {
                // Клик мимо строк (пустое место, заголовок) — меню не показываем.
                e.Handled = true;
                return;
            }

            string preferredKey = null;
            if (_contextMenuColumn != null)
            {
                _toolColumns.TryGetValue(_contextMenuColumn, out preferredKey);
            }

            ConnectMenuItem.Items.Clear();
            foreach (var key in ToolKeys.All)
            {
                var id = contact.GetToolId(key);
                var hasId = !string.IsNullOrWhiteSpace(id);
                var item = new MenuItem
                {
                    Header = hasId ? ToolKeys.DisplayName(key) + "  —  " + id.Trim() : ToolKeys.DisplayName(key),
                    IsEnabled = hasId,
                    Icon = CreateToolIcon(key),
                    FontWeight = key == preferredKey && hasId ? FontWeights.SemiBold : FontWeights.Normal,
                };

                var toolKey = key;
                item.Click += (_, _) => _viewModel.Connect(toolKey, contact);
                ConnectMenuItem.Items.Add(item);
            }

            ConnectMenuItem.IsEnabled = ToolKeys.All.Any(key => !string.IsNullOrWhiteSpace(contact.GetToolId(key)));
        }

        /// <summary>Иконка программы из панели над таблицей (если её удалось извлечь из exe).</summary>
        private Image CreateToolIcon(string toolKey)
        {
            var status = _viewModel.ToolStatuses.FirstOrDefault(s => s.Key == toolKey);
            if (status?.Icon == null)
            {
                return null;
            }

            var image = new Image { Source = status.Icon, Width = 16, Height = 16 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        private static T FindParent<T>(DependencyObject element)
            where T : DependencyObject
        {
            while (element != null)
            {
                if (element is T match)
                {
                    return match;
                }

                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return null;
        }
    }
}
