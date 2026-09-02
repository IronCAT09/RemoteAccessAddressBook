using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using RemouteAddressBook.Models;

namespace RemouteAddressBook.Services
{
    /// <summary>Поиск установленных программ удалённого доступа по стандартным путям.</summary>
    public static class ToolDiscoveryService
    {
        /// <summary>Имена исполняемых файлов, которые ищем для каждого инструмента.</summary>
        private static readonly Dictionary<string, string[]> ExeNames = new Dictionary<string, string[]>
        {
            [ToolKeys.AnyDesk] = new[] { "AnyDesk.exe" },
            [ToolKeys.Rudesktop] = new[] { "RuDesktop.exe", "rudesktop.exe", "Rudesktop.exe" },
            [ToolKeys.Assistant] = new[] { "assistant.exe", "Assistant.exe", "Assistant_host.exe" },
            [ToolKeys.Ammyy] = new[] { "AA_v3.exe", "Ammyy_Admin.exe", "AMMYY_Admin.exe", "AA_v3.7.exe" },
            [ToolKeys.Rdp] = new[] { "mstsc.exe" },
        };

        /// <summary>Названия каталогов, в которых обычно лежит программа.</summary>
        private static readonly Dictionary<string, string[]> FolderNames = new Dictionary<string, string[]>
        {
            [ToolKeys.AnyDesk] = new[] { "AnyDesk" },
            [ToolKeys.Rudesktop] = new[] { "RuDesktop", "Rudesktop", "RU Desktop" },
            [ToolKeys.Assistant] = new[] { "Assistant", "Ассистент", "Safib Assistant", "Safib" },
            [ToolKeys.Ammyy] = new[] { "Ammyy", "AmmyyAdmin", "Ammyy Admin" },
            [ToolKeys.Rdp] = new string[0],
        };

        /// <summary>Фрагменты названий в списке установленных программ Windows.</summary>
        private static readonly Dictionary<string, string[]> DisplayNameHints = new Dictionary<string, string[]>
        {
            [ToolKeys.AnyDesk] = new[] { "anydesk" },
            [ToolKeys.Rudesktop] = new[] { "rudesktop", "ru desktop" },
            [ToolKeys.Assistant] = new[] { "ассистент", "assistant" },
            [ToolKeys.Ammyy] = new[] { "ammyy" },
            [ToolKeys.Rdp] = new string[0],
        };

        private static readonly string[] UninstallKeys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        /// <summary>Ищет все известные инструменты. Возвращает найденные пути по ключу инструмента.</summary>
        public static Dictionary<string, string> DiscoverAll()
        {
            var result = new Dictionary<string, string>();
            foreach (var key in ToolKeys.All)
            {
                var path = Discover(key);
                if (!string.IsNullOrEmpty(path))
                {
                    result[key] = path;
                }
            }

            return result;
        }

        /// <summary>
        /// Ищет исполняемый файл одного инструмента: реестр App Paths, список установленных
        /// программ, стандартные каталоги, затем PATH. Возвращает null, если не нашли.
        /// </summary>
        public static string Discover(string toolKey)
        {
            if (!ExeNames.TryGetValue(toolKey, out var exeNames))
            {
                return null;
            }

            return FindInAppPaths(exeNames)
                   ?? FindInUninstallKeys(toolKey, exeNames)
                   ?? FindInStandardFolders(toolKey, exeNames)
                   ?? FindInPath(exeNames);
        }

        /// <summary>
        /// Заполняет пути в настройках. По умолчанию трогает только те инструменты,
        /// у которых путь пуст или файл не найден. Возвращает список изменённых инструментов.
        /// </summary>
        public static List<string> FillMissing(AppSettings settings, bool overwriteExisting = false)
        {
            var updated = new List<string>();
            if (settings == null)
            {
                return updated;
            }

            settings.ApplyToolDefaults();
            foreach (var key in ToolKeys.All)
            {
                var tool = settings.GetTool(key);
                if (!overwriteExisting && IsUsablePath(tool.ExePath))
                {
                    continue;
                }

                var found = Discover(key);
                if (string.IsNullOrEmpty(found) ||
                    string.Equals(found, tool.ExePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                tool.ExePath = found;
                settings.Tools[key] = tool;
                updated.Add(key);
            }

            return updated;
        }

        /// <summary>Путь указывает на существующий файл или на файл, который найдётся в PATH.</summary>
        public static bool IsUsablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            path = path.Trim();
            if (File.Exists(path))
            {
                return true;
            }

            // Голое имя файла имеет смысл, только если оно есть в PATH.
            return !Path.IsPathRooted(path) && FindInPath(new[] { path }) != null;
        }

        /// <summary>HKLM/HKCU ...\App Paths\&lt;exe&gt; — путь, который Windows использует для «Выполнить».</summary>
        private static string FindInAppPaths(IEnumerable<string> exeNames)
        {
            foreach (var exeName in exeNames)
            {
                foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    var path = ReadAppPath(root, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName)
                               ?? ReadAppPath(root, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + exeName);
                    if (path != null)
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private static string ReadAppPath(RegistryKey root, string subKey)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                var value = key?.GetValue(string.Empty) as string;
                value = CleanPath(value);
                return value != null && File.Exists(value) ? value : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Список установленных программ Windows: InstallLocation и DisplayIcon.</summary>
        private static string FindInUninstallKeys(string toolKey, string[] exeNames)
        {
            if (!DisplayNameHints.TryGetValue(toolKey, out var hints) || hints.Length == 0)
            {
                return null;
            }

            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var uninstallKey in UninstallKeys)
                {
                    var found = ScanUninstallKey(root, uninstallKey, hints, exeNames);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static string ScanUninstallKey(RegistryKey root, string subKey, string[] hints, string[] exeNames)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null)
                {
                    return null;
                }

                foreach (var name in key.GetSubKeyNames())
                {
                    try
                    {
                        using var entry = key.OpenSubKey(name);
                        var displayName = entry?.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(displayName) || !MatchesAnyHint(displayName, hints))
                        {
                            continue;
                        }

                        var installLocation = CleanPath(entry.GetValue("InstallLocation") as string);
                        if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                        {
                            var exe = FindExeInFolder(installLocation, exeNames, 2);
                            if (exe != null)
                            {
                                return exe;
                            }
                        }

                        var displayIcon = CleanPath(entry.GetValue("DisplayIcon") as string);
                        if (!string.IsNullOrEmpty(displayIcon) &&
                            displayIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(displayIcon))
                        {
                            return displayIcon;
                        }
                    }
                    catch (Exception)
                    {
                        // Недоступная запись реестра — пропускаем.
                    }
                }
            }
            catch (Exception)
            {
                // Нет доступа к ветке — пропускаем.
            }

            return null;
        }

        private static bool MatchesAnyHint(string displayName, string[] hints)
        {
            foreach (var hint in hints)
            {
                if (displayName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Стандартные каталоги установки и портативных сборок.</summary>
        private static string FindInStandardFolders(string toolKey, string[] exeNames)
        {
            var folderNames = FolderNames.TryGetValue(toolKey, out var names) ? names : new string[0];

            foreach (var root in StandardRoots())
            {
                if (string.IsNullOrEmpty(root) || !SafeDirectoryExists(root))
                {
                    continue;
                }

                // Прямо в корне (портативные сборки: Рабочий стол, Загрузки, System32).
                var direct = FindExeInFolder(root, exeNames, 0);
                if (direct != null)
                {
                    return direct;
                }

                // В каталоге программы, при необходимости — на уровень глубже (версии, Programs).
                foreach (var folderName in folderNames)
                {
                    var folder = Path.Combine(root, folderName);
                    if (!SafeDirectoryExists(folder))
                    {
                        continue;
                    }

                    var exe = FindExeInFolder(folder, exeNames, 2);
                    if (exe != null)
                    {
                        return exe;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> StandardRoots()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            yield return SettingsService.AppDirectory;
        }

        /// <summary>
        /// Ищет файл в каталоге и, если depth больше нуля, в его подкаталогах
        /// (без обхода всего диска).
        /// </summary>
        private static string FindExeInFolder(string folder, string[] exeNames, int depth)
        {
            try
            {
                foreach (var exeName in exeNames)
                {
                    var candidate = Path.Combine(folder, exeName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                if (depth <= 0)
                {
                    return null;
                }

                foreach (var subFolder in Directory.EnumerateDirectories(folder))
                {
                    var found = FindExeInFolder(subFolder, exeNames, depth - 1);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            catch (Exception)
            {
                // Нет доступа к каталогу — пропускаем.
            }

            return null;
        }

        /// <summary>Поиск по каталогам из переменной окружения PATH.</summary>
        private static string FindInPath(IEnumerable<string> exeNames)
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable))
            {
                return null;
            }

            foreach (var folder in pathVariable.Split(Path.PathSeparator))
            {
                var trimmed = folder.Trim().Trim('"');
                if (trimmed.Length == 0)
                {
                    continue;
                }

                foreach (var exeName in exeNames)
                {
                    try
                    {
                        var candidate = Path.Combine(trimmed, exeName);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch (Exception)
                    {
                        // Некорректный элемент PATH — пропускаем.
                    }
                }
            }

            return null;
        }

        private static bool SafeDirectoryExists(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Убирает кавычки и хвост вида «,0» у путей из реестра.</summary>
        private static string CleanPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim().Trim('"');
            var commaIndex = value.LastIndexOf(',');
            if (commaIndex > 0 && commaIndex > value.LastIndexOf(Path.DirectorySeparatorChar))
            {
                var tail = value.Substring(commaIndex + 1);
                if (int.TryParse(tail, out _))
                {
                    value = value.Substring(0, commaIndex).Trim().Trim('"');
                }
            }

            return value.Length == 0 ? null : value;
        }
    }
}
