using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using RemoteAccessAddressBook.Models;
using RemoteAccessAddressBook.Services;

namespace RemoteAccessAddressBook.Data
{
    /// <summary>Результат сохранения контакта, который могли изменить в другом экземпляре приложения.</summary>
    public enum SaveResult
    {
        Saved,

        /// <summary>Контакт изменили в базе после того, как он был открыт на редактирование.</summary>
        Conflict,

        /// <summary>Контакт удалили в базе.</summary>
        NotFound,
    }

    /// <summary>Параметры шифрования базы: соль и проверочный блоб для парольной фразы.</summary>
    public class EncryptionInfo
    {
        public byte[] Salt { get; set; }

        public int Iterations { get; set; }

        public byte[] Check { get; set; }
    }

    /// <summary>
    /// Доступ к базе данных SQLite. Базу могут одновременно открывать несколько экземпляров
    /// приложения (в том числе через общую сетевую папку), поэтому соединение открывается
    /// на каждую операцию и сразу закрывается, а любая запись увеличивает ревизию базы
    /// (таблица db_revision, триггеры) — по ней другие экземпляры узнают об изменениях.
    /// </summary>
    public class Database
    {
        /// <summary>Таблицы, изменения в которых должны быть видны другим экземплярам.</summary>
        private static readonly string[] TrackedTables = { "contacts", "contact_labels", "groups", "labels" };

        /// <summary>Поля контакта, которые шифруются, если шифрование базы включено.</summary>
        private static readonly string[] EncryptedColumns =
        {
            "name", "comment", "anydesk", "rudesktop", "assistant", "ammyy", "rdp", "password", "rdp_login", "rdp_password",
        };

        private const string SettingSalt = "enc_salt";
        private const string SettingIterations = "enc_iterations";
        private const string SettingCheck = "enc_check";

        private readonly string _connectionString;
        private byte[] _key;

        public Database(string databasePath)
        {
            DatabasePath = databasePath;
            var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,

                // Не держать файл на сетевой папке открытым между операциями.
                Pooling = false,

                // Пока другой экземпляр пишет, база заблокирована: ждём до 30 с, а не падаем сразу.
                DefaultTimeout = 30,
            }.ToString();
        }

        public string DatabasePath { get; }

        /// <summary>Ключ задан — новые и изменённые контакты пишутся зашифрованными.</summary>
        public bool HasKey => _key != null;

        /// <summary>Создаёт файл БД и схему, если их ещё нет, и дообновляет схему старых баз.</summary>
        public void EnsureCreated()
        {
            using var connection = OpenConnection();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS groups (
    id   INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS labels (
    id   INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS contacts (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    name         TEXT NOT NULL DEFAULT '',
    comment      TEXT NOT NULL DEFAULT '',
    anydesk      TEXT NOT NULL DEFAULT '',
    rudesktop    TEXT NOT NULL DEFAULT '',
    assistant    TEXT NOT NULL DEFAULT '',
    ammyy        TEXT NOT NULL DEFAULT '',
    rdp          TEXT NOT NULL DEFAULT '',
    password     TEXT NOT NULL DEFAULT '',
    rdp_login    TEXT NOT NULL DEFAULT '',
    rdp_password TEXT NOT NULL DEFAULT '',
    group_id     INTEGER NULL REFERENCES groups(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS contact_labels (
    contact_id INTEGER NOT NULL REFERENCES contacts(id) ON DELETE CASCADE,
    label_id   INTEGER NOT NULL REFERENCES labels(id) ON DELETE CASCADE,
    PRIMARY KEY (contact_id, label_id)
);

CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS ix_contacts_group ON contacts(group_id);

CREATE TABLE IF NOT EXISTS db_revision (
    id    INTEGER PRIMARY KEY CHECK (id = 1),
    value INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO db_revision (id, value) VALUES (1, 0);
" + RevisionTriggersSql();
                command.ExecuteNonQuery();
            }

            EnsureVersionColumn(connection);
            SetRollbackJournal(connection);
        }

        /// <summary>
        /// Ревизия базы: растёт при любом изменении контактов, групп и меток,
        /// кем бы оно ни было сделано.
        /// </summary>
        public long GetRevision()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM db_revision WHERE id = 1";
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
        }

        // ---------- Шифрование ----------

        /// <summary>Параметры шифрования, если оно включено; иначе null.</summary>
        public EncryptionInfo GetEncryptionInfo()
        {
            var salt = GetSetting(SettingSalt);
            var check = GetSetting(SettingCheck);
            if (string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(check))
            {
                return null;
            }

            return new EncryptionInfo
            {
                Salt = Convert.FromBase64String(salt),
                Iterations = int.TryParse(GetSetting(SettingIterations), out var iterations) ? iterations : VaultCrypto.DefaultIterations,
                Check = Convert.FromBase64String(check),
            };
        }

        /// <summary>Задаёт ключ для чтения и записи зашифрованных полей (после проверки фразы).</summary>
        public void SetKey(byte[] key) => _key = key;

        /// <summary>Шифрует все контакты и включает шифрование базы.</summary>
        public void EnableEncryption(byte[] key, byte[] salt, int iterations)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            WriteSetting(connection, transaction, SettingSalt, Convert.ToBase64String(salt));
            WriteSetting(connection, transaction, SettingIterations, iterations.ToString());
            WriteSetting(connection, transaction, SettingCheck, Convert.ToBase64String(VaultCrypto.CreateCheck(key)));

            RewriteAllContacts(connection, transaction, value => VaultCrypto.IsEncryptedText(value.Value)
                ? value.Value
                : VaultCrypto.EncryptText(key, value.Value, Aad(value.Column)));

            transaction.Commit();
            _key = key;

            // Открытые значения остались в освободившихся страницах файла — переписываем файл целиком.
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM;";
            vacuum.ExecuteNonQuery();
        }

        /// <summary>Расшифровывает все контакты и выключает шифрование базы.</summary>
        public void DisableEncryption()
        {
            var key = _key ?? throw new InvalidOperationException("Ключ шифрования не задан.");
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            RewriteAllContacts(connection, transaction, value => VaultCrypto.DecryptText(key, value.Value, Aad(value.Column)));

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM settings WHERE key IN ($salt, $iterations, $check)";
                command.Parameters.AddWithValue("$salt", SettingSalt);
                command.Parameters.AddWithValue("$iterations", SettingIterations);
                command.Parameters.AddWithValue("$check", SettingCheck);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            _key = null;
        }

        private static void RewriteAllContacts(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Func<(string Column, string Value), string> transform)
        {
            var rows = new List<(long Id, string[] Values)>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT id, " + string.Join(", ", EncryptedColumns) + " FROM contacts";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var values = new string[EncryptedColumns.Length];
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = reader.GetString(i + 1);
                    }

                    rows.Add((reader.GetInt64(0), values));
                }
            }

            var setClause = string.Join(", ", EncryptedColumns.Select(c => c + " = $" + c));
            foreach (var row in rows)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE contacts SET " + setClause + " WHERE id = $id";
                for (var i = 0; i < EncryptedColumns.Length; i++)
                {
                    command.Parameters.AddWithValue("$" + EncryptedColumns[i], transform((EncryptedColumns[i], row.Values[i])));
                }

                command.Parameters.AddWithValue("$id", row.Id);
                command.ExecuteNonQuery();
            }
        }

        private static string Aad(string column) => "contacts." + column;

        private string Protect(string column, string value) =>
            _key == null ? value ?? string.Empty : VaultCrypto.EncryptText(_key, value ?? string.Empty, Aad(column));

        private string Reveal(string column, string value) => VaultCrypto.DecryptText(_key, value, Aad(column));

        // ---------- Группы и метки ----------

        public List<GroupItem> LoadGroups()
        {
            var result = new List<GroupItem>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name FROM groups ORDER BY name COLLATE NOCASE";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new GroupItem(reader.GetInt64(0), reader.GetString(1), GroupKind.Normal));
            }

            return result;
        }

        public List<LabelItem> LoadLabels()
        {
            var result = new List<LabelItem>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name FROM labels ORDER BY name COLLATE NOCASE";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new LabelItem(reader.GetInt64(0), reader.GetString(1)));
            }

            return result;
        }

        /// <summary>Id группы с таким именем (без учёта регистра); создаёт её, если нет. null для пустого имени.</summary>
        public long? EnsureGroup(string name) => EnsureNamed("groups", name);

        /// <summary>Id метки с таким именем (без учёта регистра); создаёт её, если нет.</summary>
        public long EnsureLabel(string name) => EnsureNamed("labels", name) ?? throw new ArgumentException("Пустое имя метки.");

        private long? EnsureNamed(string table, string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = "SELECT id FROM " + table + " WHERE name = $name COLLATE NOCASE LIMIT 1";
                find.Parameters.AddWithValue("$name", name);
                var existing = find.ExecuteScalar();
                if (existing != null && existing != DBNull.Value)
                {
                    return Convert.ToInt64(existing);
                }
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO " + table + " (name) VALUES ($name); SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$name", name);
            var id = Convert.ToInt64(insert.ExecuteScalar());
            transaction.Commit();
            return id;
        }

        public long AddGroup(string name)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO groups (name) VALUES ($name); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public void RenameGroup(long id, string name)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE groups SET name = $name WHERE id = $id";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        /// <summary>Удаляет группу; её контакты становятся «без группы».</summary>
        public void DeleteGroup(long id)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE contacts SET group_id = NULL WHERE group_id = $id";
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM groups WHERE id = $id";
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public long AddLabel(string name)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO labels (name) VALUES ($name); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$name", name);
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public void RenameLabel(long id, string name)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE labels SET name = $name WHERE id = $id";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        public void DeleteLabel(long id)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM labels WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        // ---------- Контакты ----------

        /// <summary>
        /// Загружает контакты с именами групп и меток. Сортировка — в памяти: зашифрованные
        /// имена в SQL отсортировать нельзя.
        /// </summary>
        public List<Contact> LoadContacts()
        {
            var groupNames = LoadGroups().ToDictionary(g => g.Id, g => g.Name);
            var labelNames = LoadLabels().ToDictionary(l => l.Id, l => l.Name);

            var contacts = new List<Contact>();
            var byId = new Dictionary<long, Contact>();
            using var connection = OpenConnection();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT id, name, comment, anydesk, rudesktop, assistant, ammyy, rdp, password, " +
                    "rdp_login, rdp_password, group_id, version FROM contacts";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var groupId = reader.IsDBNull(11) ? (long?)null : reader.GetInt64(11);
                    var contact = new Contact
                    {
                        Source = ContactSource.Local,
                        Id = reader.GetInt64(0),
                        Name = Reveal("name", reader.GetString(1)),
                        Comment = Reveal("comment", reader.GetString(2)),
                        AnyDesk = Reveal("anydesk", reader.GetString(3)),
                        Rudesktop = Reveal("rudesktop", reader.GetString(4)),
                        Assistant = Reveal("assistant", reader.GetString(5)),
                        Ammyy = Reveal("ammyy", reader.GetString(6)),
                        Rdp = Reveal("rdp", reader.GetString(7)),
                        Password = Reveal("password", reader.GetString(8)),
                        RdpLogin = Reveal("rdp_login", reader.GetString(9)),
                        RdpPassword = Reveal("rdp_password", reader.GetString(10)),
                        GroupId = groupId,
                        GroupName = groupId.HasValue && groupNames.TryGetValue(groupId.Value, out var groupName) ? groupName : string.Empty,
                        Version = reader.GetInt64(12),
                    };
                    contacts.Add(contact);
                    byId[contact.Id] = contact;
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT contact_id, label_id FROM contact_labels";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (byId.TryGetValue(reader.GetInt64(0), out var contact))
                    {
                        var labelId = reader.GetInt64(1);
                        contact.LabelIds.Add(labelId);
                        if (labelNames.TryGetValue(labelId, out var labelName))
                        {
                            contact.LabelNames.Add(labelName);
                        }
                    }
                }
            }

            contacts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return contacts;
        }

        /// <summary>Подставляет id групп и меток по их именам (создавая недостающие).</summary>
        public void ResolveNames(Contact contact)
        {
            contact.GroupId = EnsureGroup(contact.GroupName);
            contact.LabelIds.Clear();
            foreach (var name in contact.LabelNames)
            {
                contact.LabelIds.Add(EnsureLabel(name));
            }
        }

        public long InsertContact(Contact contact)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            long id;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO contacts (name, comment, anydesk, rudesktop, assistant, ammyy, rdp, " +
                    "password, rdp_login, rdp_password, group_id) VALUES " +
                    "($name, $comment, $anydesk, $rudesktop, $assistant, $ammyy, $rdp, " +
                    "$password, $rdpLogin, $rdpPassword, $groupId); SELECT last_insert_rowid();";
                BindContact(command, contact);
                id = Convert.ToInt64(command.ExecuteScalar());
            }

            ReplaceLabels(connection, transaction, id, contact.LabelIds);
            transaction.Commit();
            contact.Id = id;
            contact.Version = 0;
            return id;
        }

        /// <summary>
        /// Сохраняет контакт. Если передана <paramref name="expectedVersion"/> (версия строки на момент
        /// открытия окна), запись выполняется, только если строку с тех пор никто не менял.
        /// Версию увеличивает и триггер, поэтому правки из старых версий программы тоже учитываются.
        /// </summary>
        public SaveResult UpdateContact(Contact contact, long? expectedVersion = null)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "UPDATE contacts SET name = $name, comment = $comment, anydesk = $anydesk, " +
                    "rudesktop = $rudesktop, assistant = $assistant, ammyy = $ammyy, rdp = $rdp, " +
                    "password = $password, rdp_login = $rdpLogin, rdp_password = $rdpPassword, " +
                    "group_id = $groupId, version = version + 1 WHERE id = $id";
                BindContact(command, contact);
                command.Parameters.AddWithValue("$id", contact.Id);

                if (expectedVersion.HasValue)
                {
                    command.CommandText += " AND version = $version";
                    command.Parameters.AddWithValue("$version", expectedVersion.Value);
                }

                if (command.ExecuteNonQuery() == 0)
                {
                    // Транзакция откатится при выходе: ничего не записываем.
                    return ContactExists(connection, transaction, contact.Id) ? SaveResult.Conflict : SaveResult.NotFound;
                }
            }

            ReplaceLabels(connection, transaction, contact.Id, contact.LabelIds);
            transaction.Commit();
            return SaveResult.Saved;
        }

        /// <summary>Удаляет контакт. false — его уже удалили в другом экземпляре приложения.</summary>
        public bool DeleteContact(long contactId)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM contacts WHERE id = $id";
            command.Parameters.AddWithValue("$id", contactId);
            return command.ExecuteNonQuery() > 0;
        }

        private static bool ContactExists(SqliteConnection connection, SqliteTransaction transaction, long contactId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM contacts WHERE id = $id";
            command.Parameters.AddWithValue("$id", contactId);
            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }

        // ---------- Настройки в базе ----------

        public string GetSetting(string key)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        public void SetSetting(string key, string value)
        {
            using var connection = OpenConnection();
            WriteSetting(connection, null, key, value);
        }

        private static void WriteSetting(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO settings (key, value) VALUES ($key, $value) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value ?? string.Empty);
            command.ExecuteNonQuery();
        }

        // ---------- Служебное ----------

        /// <summary>
        /// Триггеры, увеличивающие ревизию. Они хранятся в самом файле базы, поэтому срабатывают
        /// и при записи из старых версий приложения.
        /// </summary>
        private static string RevisionTriggersSql()
        {
            var sql = new System.Text.StringBuilder();
            foreach (var table in TrackedTables)
            {
                foreach (var operation in new[] { "INSERT", "UPDATE", "DELETE" })
                {
                    sql.Append("CREATE TRIGGER IF NOT EXISTS trg_")
                        .Append(table).Append('_').Append(operation.ToLowerInvariant())
                        .Append("_revision AFTER ").Append(operation).Append(" ON ").Append(table)
                        .AppendLine(" BEGIN UPDATE db_revision SET value = value + 1 WHERE id = 1; END;");
                }
            }

            return sql.ToString();
        }

        /// <summary>
        /// Версия строки контакта для проверки одновременного редактирования. Колонка добавляется
        /// в старые базы; триггер увеличивает её и при записи из старых версий программы,
        /// которые о колонке не знают.
        /// </summary>
        private static void EnsureVersionColumn(SqliteConnection connection)
        {
            var hasColumn = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(contacts)";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "version", StringComparison.OrdinalIgnoreCase))
                    {
                        hasColumn = true;
                    }
                }
            }

            if (!hasColumn)
            {
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "ALTER TABLE contacts ADD COLUMN version INTEGER NOT NULL DEFAULT 0";
                    command.ExecuteNonQuery();
                }
                catch (SqliteException)
                {
                    // Колонку одновременно добавил другой экземпляр приложения.
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "CREATE TRIGGER IF NOT EXISTS trg_contacts_version AFTER UPDATE ON contacts " +
                    "WHEN NEW.version = OLD.version " +
                    "BEGIN UPDATE contacts SET version = OLD.version + 1 WHERE id = NEW.id; END;";
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// WAL не работает на сетевых папках (ему нужна общая память между процессами),
        /// поэтому база всегда в режиме обычного журнала — он полагается на блокировки файлов.
        /// </summary>
        private static void SetRollbackJournal(SqliteConnection connection)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode = DELETE;";
                command.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Режим не переключился, потому что базу сейчас держит другой экземпляр.
                // Это не мешает работе, попробуем при следующем запуске.
            }
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                // secure_delete: удалённые и перезаписанные данные затираются нулями, а не остаются в файле.
                pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA secure_delete = ON;";
                pragma.ExecuteNonQuery();
            }

            return connection;
        }

        private void BindContact(SqliteCommand command, Contact contact)
        {
            command.Parameters.AddWithValue("$name", Protect("name", contact.Name));
            command.Parameters.AddWithValue("$comment", Protect("comment", contact.Comment));
            command.Parameters.AddWithValue("$anydesk", Protect("anydesk", contact.AnyDesk));
            command.Parameters.AddWithValue("$rudesktop", Protect("rudesktop", contact.Rudesktop));
            command.Parameters.AddWithValue("$assistant", Protect("assistant", contact.Assistant));
            command.Parameters.AddWithValue("$ammyy", Protect("ammyy", contact.Ammyy));
            command.Parameters.AddWithValue("$rdp", Protect("rdp", contact.Rdp));
            command.Parameters.AddWithValue("$password", Protect("password", contact.Password));
            command.Parameters.AddWithValue("$rdpLogin", Protect("rdp_login", contact.RdpLogin));
            command.Parameters.AddWithValue("$rdpPassword", Protect("rdp_password", contact.RdpPassword));
            command.Parameters.AddWithValue("$groupId", (object)contact.GroupId ?? DBNull.Value);
        }

        private static void ReplaceLabels(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long contactId,
            IEnumerable<long> labelIds)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM contact_labels WHERE contact_id = $contactId";
                command.Parameters.AddWithValue("$contactId", contactId);
                command.ExecuteNonQuery();
            }

            foreach (var labelId in labelIds)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT OR IGNORE INTO contact_labels (contact_id, label_id) VALUES ($contactId, $labelId)";
                command.Parameters.AddWithValue("$contactId", contactId);
                command.Parameters.AddWithValue("$labelId", labelId);
                command.ExecuteNonQuery();
            }
        }
    }
}
