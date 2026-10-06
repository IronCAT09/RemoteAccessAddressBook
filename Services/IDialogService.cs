using System.Collections.Generic;
using RemoteAccessAddressBook.Data;
using RemoteAccessAddressBook.Models;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>Абстракция над модальными окнами, чтобы модель представления не знала о View.</summary>
    public interface IDialogService
    {
        /// <summary>Окно редактирования контакта. Возвращает true, если пользователь нажал «Сохранить».</summary>
        bool EditContact(Contact contact, IEnumerable<GroupItem> groups, IEnumerable<LabelItem> labels, string title);

        /// <summary>Окно ввода одной строки. Возвращает null, если пользователь отменил ввод.</summary>
        string PromptText(string title, string caption, string initialValue);

        /// <summary>Окно настроек. Возвращает true, если настройки сохранены.</summary>
        bool EditSettings(AppSettings settings);

        /// <summary>Окно импорта. Возвращает true, если данные были импортированы.</summary>
        bool ImportContacts(Database database, IList<Contact> existingContacts, IEnumerable<GroupItem> groups);

        /// <summary>Окно «О приложении».</summary>
        void ShowAbout(string version, string databasePath);

        bool Confirm(string message, string title);

        void Info(string message, string title);

        void Error(string message, string title);
    }
}
