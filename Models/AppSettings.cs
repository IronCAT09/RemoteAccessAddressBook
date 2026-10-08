using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RemoteAccessAddressBook.Models
{
    /// <summary>Подключение к облачной базе. Токен и ключ защищены DPAPI текущего пользователя.</summary>
    public class CloudSettings
    {
        /// <summary>Адрес сервера, например https://addr.example.com. Пусто — облако не используется.</summary>
        public string ServerUrl { get; set; } = string.Empty;

        public string Login { get; set; } = string.Empty;

        public bool IsAdmin { get; set; }

        /// <summary>Токен доступа (DPAPI, base64).</summary>
        public string ProtectedToken { get; set; } = string.Empty;

        /// <summary>Ключ шифрования из парольной фразы (DPAPI, base64).</summary>
        public string ProtectedKey { get; set; } = string.Empty;

        /// <summary>Галочка «Сохранить в облачную базу» в окне нового контакта по умолчанию.</summary>
        public bool AddToCloudByDefault { get; set; } = true;

        [JsonIgnore]
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl);
    }

    /// <summary>Настройки приложения (файл settings.json рядом с exe).</summary>
    public class AppSettings
    {
        public CloudSettings Cloud { get; set; } = new CloudSettings();

        /// <summary>
        /// Ключи зашифрованных локальных баз (DPAPI, base64) по полному пути к файлу базы,
        /// чтобы не спрашивать парольную фразу при каждом запуске.
        /// </summary>
        public Dictionary<string, string> LocalKeys { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Путь к файлу базы данных. Пустая строка — addressbook.db рядом с exe.</summary>
        public string DatabasePath { get; set; } = string.Empty;

        public bool ShowLabelsPanel { get; set; } = true;

        public bool ShowGroupsPanel { get; set; } = true;

        public bool ShowPasswords { get; set; }

        /// <summary>При сворачивании прятать окно и показывать значок в области уведомлений.</summary>
        public bool MinimizeToTray { get; set; }

        /// <summary>Оформление интерфейса: System / Light / Dark.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AppTheme Theme { get; set; } = AppTheme.Light;

        /// <summary>Настройки инструментов по ключу из <see cref="ToolKeys"/>.</summary>
        public Dictionary<string, ToolConfig> Tools { get; set; } = new Dictionary<string, ToolConfig>();

        /// <summary>Ширина колонок таблицы по имени колонки.</summary>
        public Dictionary<string, double> ColumnWidths { get; set; } = new Dictionary<string, double>();

        /// <summary>Порядок колонок таблицы (имена колонок слева направо), если пользователь его менял.</summary>
        public List<string> ColumnOrder { get; set; } = new List<string>();

        public double WindowLeft { get; set; } = double.NaN;

        public double WindowTop { get; set; } = double.NaN;

        public double WindowWidth { get; set; } = 1220;

        public double WindowHeight { get; set; } = 640;

        public bool WindowMaximized { get; set; }

        /// <summary>Последняя выбранная группа: -1 = «Все», -2 = «Без группы», иначе id группы.</summary>
        public long SelectedGroupId { get; set; } = -1;

        /// <summary>Имя выбранной группы (группы облачной и локальной баз сопоставляются по имени).</summary>
        public string SelectedGroupName { get; set; } = string.Empty;

        /// <summary>Создаёт настройки со значениями по умолчанию.</summary>
        public static AppSettings CreateDefault()
        {
            var settings = new AppSettings();
            settings.ApplyToolDefaults();
            return settings;
        }

        /// <summary>Дозаполняет отсутствующие настройки инструментов значениями по умолчанию.</summary>
        public void ApplyToolDefaults()
        {
            if (Tools == null)
            {
                Tools = new Dictionary<string, ToolConfig>();
            }

            // ВНИМАНИЕ: точный синтаксис командной строки следует проверить/уточнить
            // под конкретную версию каждой программы.
            AddIfMissing(ToolKeys.AnyDesk, new ToolConfig
            {
                ExePath = "anydesk.exe",
                Arguments = "{id} --with-password",
                PasswordToStdin = true,
            });

            // Rudesktop: подключение из командной строки не выполняется —
            // копируем ID в буфер обмена и просто запускаем программу.
            AddIfMissing(ToolKeys.Rudesktop, new ToolConfig
            {
                CopyIdToClipboard = true,
            });

            // Ассистент: параметр -CONNECT: из руководства (6.5, раздел 15.1) на практике
            // подключение не выполняет, поэтому тоже копируем ID и запускаем программу.
            AddIfMissing(ToolKeys.Assistant, new ToolConfig
            {
                ExePath = @"C:\Program Files (x86)\Ассистент\assistant.exe",
                CopyIdToClipboard = true,
            });

            AddIfMissing(ToolKeys.Ammyy, new ToolConfig
            {
                ExePath = "AA_v3.exe",
                Arguments = "-connect {id}",
            });

            AddIfMissing(ToolKeys.Rdp, new ToolConfig
            {
                ExePath = "mstsc.exe",
                Arguments = "/v:{id}",
                UseCmdKey = true,
            });

            MigrateAssistantConnectParameter();
        }

        /// <summary>
        /// Ранее у Ассистента по умолчанию стоял параметр -CONNECT: {id}, который
        /// подключение не выполняет. Заменяем его на копирование ID в буфер обмена.
        /// </summary>
        private void MigrateAssistantConnectParameter()
        {
            if (!Tools.TryGetValue(ToolKeys.Assistant, out var assistant) || assistant == null)
            {
                return;
            }

            if (assistant.Arguments != null &&
                assistant.Arguments.IndexOf("-CONNECT:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                assistant.Arguments = string.Empty;
                assistant.CopyIdToClipboard = true;
            }
        }

        public ToolConfig GetTool(string key)
        {
            ApplyToolDefaults();
            return Tools.TryGetValue(key, out var config) ? config : new ToolConfig();
        }

        private void AddIfMissing(string key, ToolConfig config)
        {
            if (!Tools.ContainsKey(key))
            {
                Tools[key] = config;
            }
        }
    }
}
