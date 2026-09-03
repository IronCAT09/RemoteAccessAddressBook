using System;
using System.Windows;
using Microsoft.Win32;
using RemouteAddressBook.Models;

namespace RemouteAddressBook.Services
{
    /// <summary>Переключение светлой и тёмной палитры на лету.</summary>
    public static class ThemeService
    {
        private const string PaletteKey = "PaletteName";

        /// <summary>
        /// Пакетный URI с явным указанием сборки — так словарь находится независимо от того,
        /// какая сборка стартовая.
        /// </summary>
        private static string PaletteUri(string name) =>
            "pack://application:,,,/" + typeof(ThemeService).Assembly.GetName().Name +
            ";component/Styles/" + name + ".xaml";

        /// <summary>Текущий режим (может быть System).</summary>
        public static AppTheme Current { get; private set; } = AppTheme.Light;

        /// <summary>Фактически применённая палитра: Light или Dark.</summary>
        public static AppTheme Effective { get; private set; } = AppTheme.Light;

        /// <summary>Событие смены палитры.</summary>
        public static event EventHandler ThemeChanged;

        /// <summary>Применяет палитру, подменяя словарь ресурсов приложения.</summary>
        public static void Apply(AppTheme theme)
        {
            var application = Application.Current;
            if (application == null)
            {
                return;
            }

            Current = theme;
            Effective = Resolve(theme);

            var source = new Uri(PaletteUri(Effective == AppTheme.Dark ? "Dark" : "Light"), UriKind.Absolute);
            var palette = new ResourceDictionary { Source = source };

            var dictionaries = application.Resources.MergedDictionaries;
            var replaced = false;
            for (var i = 0; i < dictionaries.Count; i++)
            {
                if (dictionaries[i].Contains(PaletteKey))
                {
                    dictionaries[i] = palette;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                dictionaries.Insert(0, palette);
            }

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Определяет, какая палитра соответствует режиму.</summary>
        public static AppTheme Resolve(AppTheme theme)
        {
            return theme == AppTheme.System ? DetectSystemTheme() : theme;
        }

        /// <summary>Читает системную настройку Windows «Режим приложения по умолчанию».</summary>
        private static AppTheme DetectSystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                if (value is int useLight)
                {
                    return useLight == 0 ? AppTheme.Dark : AppTheme.Light;
                }
            }
            catch (Exception)
            {
                // Нет доступа к реестру — считаем тему светлой.
            }

            return AppTheme.Light;
        }
    }
}
