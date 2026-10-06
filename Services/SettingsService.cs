using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteAccessAddressBook.Models;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>Загрузка и сохранение настроек в settings.json рядом с исполняемым файлом.</summary>
    public static class SettingsService
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,

            // WindowLeft/WindowTop до первого закрытия окна равны NaN: без этого флага
            // сериализация падала и настройки первой сессии не сохранялись.
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        /// <summary>Каталог, в котором лежит исполняемый файл приложения.</summary>
        public static string AppDirectory => AppContext.BaseDirectory;

        public static string SettingsPath => Path.Combine(AppDirectory, "settings.json");

        public static string DefaultDatabasePath => Path.Combine(AppDirectory, "addressbook.db");

        /// <summary>Существует ли файл настроек (false — первый запуск).</summary>
        public static bool SettingsFileExists => File.Exists(SettingsPath);

        public static AppSettings Load()
        {
            AppSettings settings = null;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                }
            }
            catch (Exception)
            {
                // Повреждённый файл настроек — стартуем со значениями по умолчанию.
                settings = null;
            }

            settings ??= new AppSettings();
            settings.ApplyToolDefaults();
            return settings;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, Options);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception)
            {
                // Настройки не критичны для работы — молча игнорируем ошибку записи.
            }
        }

        /// <summary>Возвращает фактический путь к БД с учётом настроек.</summary>
        public static string ResolveDatabasePath(AppSettings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.DatabasePath))
            {
                return DefaultDatabasePath;
            }

            var path = settings.DatabasePath.Trim();
            return Path.IsPathRooted(path) ? path : Path.Combine(AppDirectory, path);
        }
    }
}
