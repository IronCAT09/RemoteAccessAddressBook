using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using RemoteAccessAddressBook.Models;
using RemoteAccessAddressBook.Services;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>
    /// Модальное окно редактирования контакта. Группы и метки — по имени: их список общий
    /// для облачной и локальной баз.
    /// </summary>
    public partial class ContactEditWindow : Window
    {
        private const long NoGroupId = -2;

        private readonly Contact _contact;
        private readonly ContactEditOptions _options;
        private readonly ObservableCollection<LabelItem> _labels = new ObservableCollection<LabelItem>();

        public ContactEditWindow(
            Contact contact,
            IEnumerable<string> groups,
            IEnumerable<string> labels,
            string title,
            ContactEditOptions options)
        {
            InitializeComponent();

            _contact = contact;
            _options = options ?? new ContactEditOptions();
            Title = title;

            NameBox.Text = contact.Name;
            CommentBox.Text = contact.Comment;
            AnyDeskBox.Text = contact.AnyDesk;
            RudesktopBox.Text = contact.Rudesktop;
            AssistantBox.Text = contact.Assistant;
            AmmyyBox.Text = contact.Ammyy;
            PasswordBox.Text = contact.Password;
            RdpBox.Text = contact.Rdp;
            RdpLoginBox.Text = contact.RdpLogin;
            RdpPasswordBox.Text = contact.RdpPassword;

            var groupChoices = new List<GroupItem>
            {
                new GroupItem(NoGroupId, "— Без группы —", GroupKind.Ungrouped),
            };
            var id = 1;
            foreach (var name in groups ?? Enumerable.Empty<string>())
            {
                groupChoices.Add(new GroupItem(id++, name, GroupKind.Normal));
            }

            // Группа контакта могла пропасть из списка (например, её удалили) — всё равно показываем её.
            var currentGroup = (contact.GroupName ?? string.Empty).Trim();
            if (currentGroup.Length > 0 && !groupChoices.Any(g => g.Kind == GroupKind.Normal && Same(g.Name, currentGroup)))
            {
                groupChoices.Add(new GroupItem(id, currentGroup, GroupKind.Normal));
            }

            GroupComboBox.ItemsSource = groupChoices;
            GroupComboBox.SelectedItem = currentGroup.Length > 0
                ? groupChoices.First(g => g.Kind == GroupKind.Normal && Same(g.Name, currentGroup))
                : groupChoices[0];

            id = 1;
            var labelNames = new List<string>(labels ?? Enumerable.Empty<string>());
            labelNames.AddRange(contact.LabelNames.Where(l => !labelNames.Any(n => Same(n, l))));
            foreach (var name in labelNames)
            {
                _labels.Add(new LabelItem(id++, name) { IsChecked = contact.LabelNames.Contains(name) });
            }

            LabelsList.ItemsSource = _labels;

            if (_options.ShowCloudChoice)
            {
                SaveToCloudCheckBox.Visibility = Visibility.Visible;
                SaveToCloudCheckBox.IsEnabled = _options.CloudAvailable;
                SaveToCloudCheckBox.IsChecked = _options.CloudAvailable && _options.SaveToCloud;
                if (!_options.CloudAvailable && !string.IsNullOrEmpty(_options.CloudUnavailableReason))
                {
                    CloudNoteText.Text = "Недоступно: " + _options.CloudUnavailableReason + ". Контакт сохранится в локальную базу.";
                    CloudNoteText.Visibility = Visibility.Visible;
                }
            }
            else if (!string.IsNullOrEmpty(_options.SourceText))
            {
                SourceText.Text = "Хранится: " + _options.SourceText;
                SourceText.Visibility = Visibility.Visible;
            }

            NameBox.Focus();
        }

        private static bool Same(string a, string b) =>
            string.Equals(a?.Trim(), b?.Trim(), System.StringComparison.CurrentCultureIgnoreCase);

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _contact.Name = NameBox.Text.Trim();
            _contact.Comment = CommentBox.Text.Trim();
            _contact.AnyDesk = AnyDeskBox.Text.Trim();
            _contact.Rudesktop = RudesktopBox.Text.Trim();
            _contact.Assistant = AssistantBox.Text.Trim();
            _contact.Ammyy = AmmyyBox.Text.Trim();
            _contact.Password = PasswordBox.Text;
            _contact.Rdp = RdpBox.Text.Trim();
            _contact.RdpLogin = RdpLoginBox.Text.Trim();
            _contact.RdpPassword = RdpPasswordBox.Text;

            var selectedGroup = GroupComboBox.SelectedItem as GroupItem;
            _contact.GroupName = selectedGroup == null || selectedGroup.Kind != GroupKind.Normal
                ? string.Empty
                : selectedGroup.Name;

            _contact.LabelNames.Clear();
            _contact.LabelNames.UnionWith(_labels.Where(l => l.IsChecked).Select(l => l.Name));

            _options.SaveToCloud = _options.ShowCloudChoice && SaveToCloudCheckBox.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
