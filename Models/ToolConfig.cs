namespace RemouteAddressBook.Models
{
    /// <summary>Настройки запуска одного инструмента удалённого доступа.</summary>
    public class ToolConfig
    {
        /// <summary>Путь к исполняемому файлу (или просто имя, если он есть в PATH).</summary>
        public string ExePath { get; set; } = string.Empty;

        /// <summary>
        /// Шаблон аргументов командной строки.
        /// Плейсхолдеры: {id}, {password}, {login}.
        /// </summary>
        public string Arguments { get; set; } = string.Empty;

        /// <summary>Передавать пароль в stdin процесса (нужно для AnyDesk --with-password).</summary>
        public bool PasswordToStdin { get; set; }

        /// <summary>
        /// Перед запуском сохранить учётные данные через cmdkey (используется для RDP).
        /// Выполняется: cmdkey /generic:TERMSRV/{id} /user:{login} /pass:{password}
        /// </summary>
        public bool UseCmdKey { get; set; }

        /// <summary>
        /// Не передавать ID в аргументах, а скопировать его в буфер обмена и просто
        /// запустить программу (для Ассистента и RuDesktop — их командная строка
        /// подключение не выполняет).
        /// </summary>
        public bool CopyIdToClipboard { get; set; }
    }
}
