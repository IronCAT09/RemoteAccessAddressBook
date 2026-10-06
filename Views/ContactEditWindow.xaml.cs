using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using RemoteAccessAddressBook.Models;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>Модальное окно редактирования контакта.</summary>
    public partial class ContactEditWindow : Window
    {
        private const long NoGroupId = -2;

        private readonly Contact _contact;
        private readonly ObservableCollection<LabelItem> _labels = new ObservableCollection<LabelItem>();

        public ContactEditWindow(Contact contact, IEnumerable<GroupItem> groups, IEnumerable<LabelItem> labels, string title)
        {
            InitializeComponent();

            _contact = contact;
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
            if (groups != null)
            {
                groupChoices.AddRange(groups.Where(g => !g.IsSystem));
            }

            GroupComboBox.ItemsSource = groupChoices;
            GroupComboBox.SelectedItem = contact.GroupId.HasValue
                ? groupChoices.FirstOrDefault(g => g.Kind == GroupKind.Normal && g.Id == contact.GroupId.Value)
                  ?? groupChoices[0]
                : groupChoices[0];

            if (labels != null)
            {
                foreach (var label in labels)
                {
                    _labels.Add(new LabelItem(label.Id, label.Name)
                    {
                        IsChecked = contact.LabelIds.Contains(label.Id),
                    });
                }
            }

            LabelsList.ItemsSource = _labels;
            NameBox.Focus();
        }

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
            _contact.GroupId = selectedGroup == null || selectedGroup.Kind != GroupKind.Normal
                ? (long?)null
                : selectedGroup.Id;

            _contact.LabelIds.Clear();
            foreach (var label in _labels.Where(l => l.IsChecked))
            {
                _contact.LabelIds.Add(label.Id);
            }

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
