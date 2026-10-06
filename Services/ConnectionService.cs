using System;
using System.ComponentModel;
using System.Diagnostics;
using RemoteAccessAddressBook.Models;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>Результат попытки запуска подключения.</summary>
    public class ConnectionResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public static ConnectionResult Ok(string message = null) =>
            new ConnectionResult { Success = true, Message = message ?? string.Empty };

        public static ConnectionResult Fail(string message) =>
            new ConnectionResult { Success = false, Message = message };
    }

    /// <summary>Запуск внешних программ удалённого доступа по шаблонам из настроек.</summary>
    public static class ConnectionService
    {
        /// <summary>
        /// Запускает подключение для указанного инструмента.
        /// Возвращает результат с понятным текстом ошибки, если запустить не удалось.
        /// </summary>
        public static ConnectionResult Connect(string toolKey, Contact contact, AppSettings settings)
        {
            if (contact == null || settings == null)
            {
                return ConnectionResult.Fail("Нет данных для подключения.");
            }

            var id = contact.GetToolId(toolKey);
            if (string.IsNullOrWhiteSpace(id))
            {
                // Пустая ячейка — ничего не делаем.
                return ConnectionResult.Ok();
            }

            id = id.Trim();
            var tool = settings.GetTool(toolKey);
            var toolName = ToolKeys.DisplayName(toolKey);

            // Режим «скопировать ID и запустить» работает и без заданного пути:
            // ID всё равно попадает в буфер обмена.
            if (tool.CopyIdToClipboard)
            {
                return LaunchWithClipboard(tool, toolName, id);
            }

            if (string.IsNullOrWhiteSpace(tool.ExePath))
            {
                return ConnectionResult.Fail(
                    "Для «" + toolName + "» не задан путь к исполняемому файлу.\n" +
                    "Откройте Настройки и укажите программу и шаблон аргументов.");
            }

            var password = toolKey == ToolKeys.Rdp ? contact.RdpPassword : contact.Password;
            var login = contact.RdpLogin;

            if (tool.UseCmdKey && !string.IsNullOrWhiteSpace(login))
            {
                var cmdKeyResult = RunCmdKey(id, login, password);
                if (!cmdKeyResult.Success)
                {
                    return cmdKeyResult;
                }
            }

            var arguments = BuildArguments(tool.Arguments, id, password, login);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = tool.ExePath,
                    Arguments = arguments,
                    UseShellExecute = !tool.PasswordToStdin,
                    RedirectStandardInput = tool.PasswordToStdin,
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    return ConnectionResult.Fail("Не удалось запустить «" + toolName + "».");
                }

                if (tool.PasswordToStdin)
                {
                    if (!string.IsNullOrEmpty(password))
                    {
                        process.StandardInput.WriteLine(password);
                    }

                    process.StandardInput.Close();
                }

                return ConnectionResult.Ok();
            }
            catch (Win32Exception ex)
            {
                return ConnectionResult.Fail(
                    "Не удалось запустить «" + toolName + "».\n" +
                    "Файл не найден или недоступен: " + tool.ExePath + "\n\n" +
                    "Проверьте путь в настройках.\n" +
                    "Подробности: " + ex.Message);
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail(
                    "Ошибка при запуске «" + toolName + "»: " + ex.Message);
            }
        }

        /// <summary>
        /// Просто запускает программу без ID и аргументов — по клику на её иконке.
        /// </summary>
        public static ConnectionResult LaunchTool(string toolKey, AppSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(toolKey))
            {
                return ConnectionResult.Fail("Нет данных для запуска.");
            }

            var toolName = ToolKeys.DisplayName(toolKey);
            var tool = settings.GetTool(toolKey);
            var exePath = ToolDiscoveryService.ResolveExecutable(tool.ExePath);

            if (exePath == null)
            {
                return ConnectionResult.Fail(
                    "«" + toolName + "» не найден на компьютере.\n" +
                    (string.IsNullOrWhiteSpace(tool.ExePath)
                        ? "Путь не задан."
                        : "Указанный путь: " + tool.ExePath) + "\n\n" +
                    "Откройте Настройки и укажите путь или нажмите «Найти установленные».");
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                });
            }
            catch (Win32Exception ex)
            {
                return ConnectionResult.Fail(
                    "Запустить «" + toolName + "» не удалось: " + exePath + "\n" + ex.Message);
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail("Запустить «" + toolName + "» не удалось: " + ex.Message);
            }

            return ConnectionResult.Ok("«" + toolName + "» запущен.");
        }

        /// <summary>
        /// Копирует ID в буфер обмена и запускает программу без передачи ID в аргументах.
        /// Используется там, где командная строка подключение не выполняет.
        /// </summary>
        private static ConnectionResult LaunchWithClipboard(ToolConfig tool, string toolName, string id)
        {
            // Даже если буфер обмена занят другим приложением, программу всё равно запускаем.
            var copied = TryCopyToClipboard(id);

            if (string.IsNullOrWhiteSpace(tool.ExePath))
            {
                return copied
                    ? ConnectionResult.Ok(
                        "ID " + id + " скопирован в буфер обмена. Путь к «" + toolName +
                        "» не задан — программа не запущена.")
                    : ConnectionResult.Fail(
                        "Не удалось скопировать ID в буфер обмена — он занят другим приложением.\n" +
                        "Путь к «" + toolName + "» тоже не задан.");
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = tool.ExePath,
                    Arguments = tool.Arguments ?? string.Empty,
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
            }
            catch (Win32Exception ex)
            {
                return ConnectionResult.Fail(
                    "Запустить «" + toolName + "» не удалось.\n" +
                    "Файл не найден или недоступен: " + tool.ExePath + "\n\n" +
                    "Проверьте путь в настройках.\n" +
                    "Подробности: " + ex.Message);
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail("Запустить «" + toolName + "» не удалось: " + ex.Message);
            }

            return copied
                ? ConnectionResult.Ok("ID " + id + " скопирован в буфер обмена, «" + toolName + "» запущен.")
                : ConnectionResult.Fail(
                    "«" + toolName + "» запущен, но скопировать ID в буфер обмена не удалось —\n" +
                    "буфер занят другим приложением. ID: " + id);
        }

        private static bool TryCopyToClipboard(string text) => ClipboardText.TrySet(text);

        /// <summary>Подставляет значения вместо плейсхолдеров {id}, {password}, {login}.</summary>
        public static string BuildArguments(string template, string id, string password, string login)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            return template
                .Replace("{id}", id ?? string.Empty)
                .Replace("{password}", password ?? string.Empty)
                .Replace("{login}", login ?? string.Empty);
        }

        /// <summary>Сохраняет учётные данные RDP через cmdkey, чтобы mstsc не спрашивал пароль.</summary>
        private static ConnectionResult RunCmdKey(string host, string login, string password)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmdkey.exe",
                    Arguments = "/generic:TERMSRV/" + host + " /user:" + login + " /pass:" + (password ?? string.Empty),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit(5000);
                return ConnectionResult.Ok();
            }
            catch (Win32Exception ex)
            {
                return ConnectionResult.Fail(
                    "Не удалось выполнить cmdkey.exe для сохранения учётных данных RDP.\n" +
                    "Подробности: " + ex.Message);
            }
            catch (Exception ex)
            {
                return ConnectionResult.Fail("Ошибка cmdkey: " + ex.Message);
            }
        }
    }
}
