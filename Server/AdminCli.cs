namespace RemoteAccessAddressBook.Server
{
    /// <summary>
    /// Управление пользователями из консоли сервера:
    ///   raab-server user add &lt;логин&gt; [--admin]
    ///   raab-server user passwd &lt;логин&gt;
    ///   raab-server user disable|enable &lt;логин&gt;
    ///   raab-server user admin|noadmin &lt;логин&gt;
    ///   raab-server user delete &lt;логин&gt;
    ///   raab-server user list
    ///   raab-server vault reset --yes
    /// Пароль вводится с клавиатуры (скрыто) или читается из стандартного ввода.
    /// </summary>
    public static class AdminCli
    {
        public static bool IsCommand(string[] args) =>
            args.Length > 0 && (args[0] == "user" || args[0] == "vault" || args[0] == "help" || args[0] == "--help");

        public static int Run(string[] args, ServerDatabase database)
        {
            try
            {
                // Консоль Windows по умолчанию в OEM-кодировке — без этого кириллица превращается в мусор.
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (IOException)
            {
            }

            try
            {
                switch (args[0])
                {
                    case "user":
                        return RunUser(args.Skip(1).ToArray(), database);
                    case "vault":
                        return RunVault(args.Skip(1).ToArray(), database);
                    default:
                        PrintHelp();
                        return 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Ошибка: " + ex.Message);
                return 1;
            }
        }

        private static int RunUser(string[] args, ServerDatabase database)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 1;
            }

            var command = args[0];
            if (command == "list")
            {
                var users = database.ListUsers();
                if (users.Count == 0)
                {
                    Console.WriteLine("Пользователей нет. Создайте первого: raab-server user add <логин> --admin");
                }

                foreach (var user in users)
                {
                    Console.WriteLine(user.Login + (user.IsAdmin ? "  [администратор]" : string.Empty) + (user.Disabled ? "  [заблокирован]" : string.Empty));
                }

                return 0;
            }

            if (args.Length < 2)
            {
                Console.Error.WriteLine("Укажите логин.");
                return 1;
            }

            var login = args[1].Trim();
            switch (command)
            {
                case "add":
                {
                    if (database.FindUser(login) != null)
                    {
                        Console.Error.WriteLine("Пользователь «" + login + "» уже есть.");
                        return 1;
                    }

                    var password = ReadNewPassword();
                    database.AddUser(login, PasswordHasher.Hash(password), args.Contains("--admin"));
                    Console.WriteLine("Пользователь «" + login + "» создан" + (args.Contains("--admin") ? " (администратор)." : "."));
                    return 0;
                }

                case "passwd":
                    return Report(database.SetPassword(login, PasswordHasher.Hash(ReadNewPassword())), login,
                        "Пароль изменён, все сеансы пользователя завершены.");
                case "disable":
                    return Report(database.SetDisabled(login, true), login, "Пользователь заблокирован, его сеансы завершены.");
                case "enable":
                    return Report(database.SetDisabled(login, false), login, "Пользователь разблокирован.");
                case "admin":
                    return Report(database.SetAdmin(login, true), login, "Пользователь стал администратором.");
                case "noadmin":
                    return Report(database.SetAdmin(login, false), login, "Права администратора сняты.");
                case "delete":
                    return Report(database.DeleteUser(login), login, "Пользователь удалён.");
                default:
                    PrintHelp();
                    return 1;
            }
        }

        private static int RunVault(string[] args, ServerDatabase database)
        {
            if (args.Length >= 1 && args[0] == "reset")
            {
                if (!args.Contains("--yes"))
                {
                    Console.Error.WriteLine(
                        "Сброс удалит ВСЕ контакты облачной базы и парольную фразу. Повторите с --yes, если уверены.");
                    return 1;
                }

                database.ResetVault();
                Console.WriteLine("Хранилище очищено. Новую парольную фразу задаст первый вошедший администратор.");
                return 0;
            }

            PrintHelp();
            return 1;
        }

        private static int Report(bool found, string login, string message)
        {
            if (!found)
            {
                Console.Error.WriteLine("Пользователь «" + login + "» не найден.");
                return 1;
            }

            Console.WriteLine(message);
            return 0;
        }

        private static string ReadNewPassword()
        {
            if (Console.IsInputRedirected)
            {
                var piped = Console.ReadLine();
                Validate(piped);
                return piped;
            }

            var first = ReadHidden("Пароль: ");
            Validate(first);
            var second = ReadHidden("Ещё раз: ");
            if (first != second)
            {
                throw new InvalidOperationException("Пароли не совпадают.");
            }

            return first;
        }

        private static void Validate(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8)
            {
                throw new InvalidOperationException("Пароль должен быть не короче 8 символов.");
            }
        }

        private static string ReadHidden(string prompt)
        {
            Console.Write(prompt);
            var buffer = new System.Text.StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return buffer.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                    }

                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    buffer.Append(key.KeyChar);
                }
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"Remote Access — Address Book, сервер облачной базы.

Запуск сервера:            raab-server
Пользователи:
  raab-server user add <логин> [--admin]   создать (пароль спросит)
  raab-server user passwd <логин>          сменить пароль (завершает сеансы)
  raab-server user disable <логин>         заблокировать (завершает сеансы)
  raab-server user enable <логин>          разблокировать
  raab-server user admin|noadmin <логин>   дать/снять права администратора
  raab-server user delete <логин>          удалить
  raab-server user list                    список
Хранилище:
  raab-server vault reset --yes            удалить все контакты и парольную фразу

Пароль можно передать через стандартный ввод: echo 'пароль' | raab-server user add ivan");
        }
    }
}
