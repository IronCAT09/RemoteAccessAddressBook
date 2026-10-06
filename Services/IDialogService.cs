using System.Collections.Generic;
using RemoteAccessAddressBook.Data;
using RemoteAccessAddressBook.Models;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>Дополнительные параметры окна контакта.</summary>
    public class ContactEditOptions
    {
        /// <summary>Показывать галочку «Сохранить в облачную базу» (только для нового контакта).</summary>
        public bool ShowCloudChoice { get; set; }

        /// <summary>Галочку можно поставить (облако подключено и доступно).</summary>
        public bool CloudAvailable { get; set; }

        /// <summary>Пояснение, если облако недоступно.</summary>
        public string CloudUnavailableReason { get; set; }

        /// <summary>Состояние галочки: на входе — по умолчанию, на выходе — выбор пользователя.</summary>
        public bool SaveToCloud { get; set; }

        /// <summary>Подпись «Где хранится» для существующего контакта.</summary>
        public string SourceText { get; set; }
    }

    /// <summary>Вопрос о расхождении: два варианта контакта рядом и кнопки выбора.</summary>
    public class ConflictRequest
    {
        public string Title { get; set; }

        public string Message { get; set; }

        public string LeftTitle { get; set; }

        public Contact Left { get; set; }

        public string RightTitle { get; set; }

        public Contact Right { get; set; }

        public string LeftButton { get; set; }

        public string RightButton { get; set; }

        /// <summary>null — кнопки «оставить оба» нет.</summary>
        public string BothButton { get; set; }

        /// <summary>Показывать галочку «Применить ко всем остальным».</summary>
        public bool ShowApplyToAll { get; set; }
    }

    public enum ConflictChoice
    {
        Left,
        Right,
        Both,
        Cancel,
    }

    public class ConflictDecision
    {
        public ConflictChoice Choice { get; set; }

        public bool ApplyToAll { get; set; }
    }

    /// <summary>Результат окна входа на сервер.</summary>
    public class CloudLoginOutcome
    {
        public string ServerUrl { get; set; }

        public CloudLoginResult Login { get; set; }
    }

    /// <summary>Абстракция над модальными окнами, чтобы модель представления не знала о View.</summary>
    public interface IDialogService
    {
        /// <summary>Окно редактирования контакта. Возвращает true, если пользователь нажал «Сохранить».</summary>
        bool EditContact(Contact contact, IEnumerable<string> groups, IEnumerable<string> labels, string title, ContactEditOptions options);

        /// <summary>Окно ввода одной строки. Возвращает null, если пользователь отменил ввод.</summary>
        string PromptText(string title, string caption, string initialValue);

        /// <summary>Окно ввода парольной фразы (скрытый ввод). confirm — ввести дважды. null — отмена.</summary>
        string PromptPassphrase(string title, string message, bool confirm);

        /// <summary>Окно входа на сервер облачной базы. null — отмена.</summary>
        CloudLoginOutcome LoginToCloud(string serverUrl, string login);

        /// <summary>Вопрос о расхождении двух вариантов контакта.</summary>
        ConflictDecision ResolveConflict(ConflictRequest request);

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
