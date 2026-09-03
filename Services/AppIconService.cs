using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RemouteAddressBook.Services
{
    /// <summary>Извлечение иконок из исполняемых файлов (без сторонних библиотек).</summary>
    public static class AppIconService
    {
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(
            string szFileName,
            int nIconIndex,
            out IntPtr phiconLarge,
            out IntPtr phiconSmall,
            uint nIcons);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>Возвращает иконку файла или null, если извлечь не удалось.</summary>
        public static ImageSource GetIcon(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            if (Cache.TryGetValue(exePath, out var cached))
            {
                return cached;
            }

            var icon = ExtractIcon(exePath);
            Cache[exePath] = icon;
            return icon;
        }

        /// <summary>Сбрасывает кэш — например, после смены путей в настройках.</summary>
        public static void ClearCache()
        {
            Cache.Clear();
        }

        private static ImageSource ExtractIcon(string exePath)
        {
            var largeIcon = IntPtr.Zero;
            var smallIcon = IntPtr.Zero;
            try
            {
                if (ExtractIconEx(exePath, 0, out largeIcon, out smallIcon, 1) == 0 || largeIcon == IntPtr.Zero)
                {
                    return null;
                }

                var image = Imaging.CreateBitmapSourceFromHIcon(
                    largeIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (largeIcon != IntPtr.Zero)
                {
                    DestroyIcon(largeIcon);
                }

                if (smallIcon != IntPtr.Zero)
                {
                    DestroyIcon(smallIcon);
                }
            }
        }
    }
}
