using System.Collections.Generic;
using System.Windows;
using RemoteAccessAddressBook.Data;
using RemoteAccessAddressBook.Models;
using RemoteAccessAddressBook.Services;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>Реализация IDialogService поверх окон WPF.</summary>
    public class DialogService : IDialogService
    {
        private readonly Window _owner;

        public DialogService(Window owner)
        {
            _owner = owner;
        }

        public bool EditContact(Contact contact, IEnumerable<GroupItem> groups, IEnumerable<LabelItem> labels, string title)
        {
            var window = new ContactEditWindow(contact, groups, labels, title) { Owner = _owner };
            return window.ShowDialog() == true;
        }

        public string PromptText(string title, string caption, string initialValue)
        {
            var window = new TextPromptWindow(title, caption, initialValue) { Owner = _owner };
            return window.ShowDialog() == true ? window.Value : null;
        }

        public bool EditSettings(AppSettings settings)
        {
            var window = new SettingsWindow(settings) { Owner = _owner };
            return window.ShowDialog() == true;
        }

        public bool ImportContacts(Database database, IList<Contact> existingContacts, IEnumerable<GroupItem> groups)
        {
            var window = new ImportWindow(database, existingContacts, groups) { Owner = _owner };
            window.ShowDialog();
            return window.Imported;
        }

        public void ShowAbout(string version, string databasePath)
        {
            var window = new AboutWindow(version, databasePath) { Owner = _owner };
            window.ShowDialog();
        }

        public bool Confirm(string message, string title)
        {
            return Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public void Info(string message, string title)
        {
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void Error(string message, string title)
        {
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>Показывает сообщение; владелец указывается, только если окно уже отображено.</summary>
        private MessageBoxResult Show(string message, string title, MessageBoxButton button, MessageBoxImage icon)
        {
            return _owner != null && _owner.IsLoaded
                ? MessageBox.Show(_owner, message, title, button, icon)
                : MessageBox.Show(message, title, button, icon);
        }
    }
}
