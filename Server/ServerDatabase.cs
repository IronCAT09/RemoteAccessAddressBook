using Microsoft.Data.Sqlite;

namespace RemoteAccessAddressBook.Server
{
    public record UserRecord(long Id, string Login, string PasswordHash, bool IsAdmin, bool Disabled);

    /// <summary>Запись хранилища. Data — шифротекст: сервер не знает, что внутри.</summary>
    public record ItemRecord(string Id, long Version, bool Deleted, byte[] Data, long Seq, string UpdatedAt, string UpdatedBy);

    public record VaultRecord(byte[] Salt, int Iterations, byte[] Check);

    public enum WriteOutcome
    {
        Ok,

        /// <summary>Запись изменили после того, как клиент её прочитал (версия не совпала).</summary>
        Conflict,
    }

    /// <summary>
    /// База сервера (SQLite на локальном диске VPS). Хранит пользователей, хэши токенов,
    /// параметры хранилища (соль и проверочный блоб парольной фразы) и записи-шифротексты.
    /// Каждое изменение записи получает новый номер seq — по нему клиенты забирают только изменения.
    /// </summary>
    public class ServerDatabase
    {
        private readonly string _connectionString;

        public ServerDatabase(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 30,
            }.ToString();
        }

        public void EnsureCreated()
        {
            using var connection = Open();
            Execute(connection, null, @"
PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS users (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    login         TEXT NOT NULL UNIQUE COLLATE NOCASE,
    password_hash TEXT NOT NULL,
    is_admin      INTEGER NOT NULL DEFAULT 0,
    disabled      INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS tokens (
    token_hash TEXT PRIMARY KEY,
    user_id    INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS items (
    id         TEXT PRIMARY KEY,
    version    INTEGER NOT NULL,
    deleted    INTEGER NOT NULL DEFAULT 0,
    data       BLOB NULL,
    seq        INTEGER NOT NULL,
    updated_at TEXT NOT NULL,
    updated_by TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_items_seq ON items(seq);

INSERT OR IGNORE INTO meta (key, value) VALUES ('seq', '0');
");
        }

        // ---------- Пользователи ----------

        public UserRecord FindUser(string login)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, login, password_hash, is_admin, disabled FROM users WHERE login = $login";
            command.Parameters.AddWithValue("$login", login ?? string.Empty);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadUser(reader) : null;
        }

        public List<UserRecord> ListUsers()
        {
            var result = new List<UserRecord>();
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, login, password_hash, is_admin, disabled FROM users ORDER BY login";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(ReadUser(reader));
            }

            return result;
        }

        public void AddUser(string login, string passwordHash, bool isAdmin)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO users (login, password_hash, is_admin, created_at) VALUES ($login, $hash, $admin, $now)";
            command.Parameters.AddWithValue("$login", login);
            command.Parameters.AddWithValue("$hash", passwordHash);
            command.Parameters.AddWithValue("$admin", isAdmin ? 1 : 0);
            command.Parameters.AddWithValue("$now", Now());
            command.ExecuteNonQuery();
        }

        /// <summary>Меняет пароль и отзывает все токены пользователя.</summary>
        public bool SetPassword(string login, string passwordHash) =>
            UpdateUserAndRevoke(login, "password_hash = $value", passwordHash);

        /// <summary>Блокирует/разблокирует пользователя; при блокировке все его токены отзываются.</summary>
        public bool SetDisabled(string login, bool disabled) =>
            UpdateUserAndRevoke(login, "disabled = $value", disabled ? 1 : 0);

        public bool SetAdmin(string login, bool isAdmin) =>
            UpdateUserAndRevoke(login, "is_admin = $value", isAdmin ? 1 : 0, revoke: false);

        public bool DeleteUser(string login)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM users WHERE login = $login";
            command.Parameters.AddWithValue("$login", login);
            return command.ExecuteNonQuery() > 0;
        }

        private bool UpdateUserAndRevoke(string login, string assignment, object value, bool revoke = true)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE users SET " + assignment + " WHERE login = $login";
                command.Parameters.AddWithValue("$login", login);
                command.Parameters.AddWithValue("$value", value);
                if (command.ExecuteNonQuery() == 0)
                {
                    return false;
                }
            }

            if (revoke)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM tokens WHERE user_id = (SELECT id FROM users WHERE login = $login)";
                command.Parameters.AddWithValue("$login", login);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }

        // ---------- Токены ----------

        public void AddToken(string tokenHash, long userId, DateTime expiresAt)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO tokens (token_hash, user_id, created_at, expires_at) VALUES ($hash, $user, $now, $expires)";
            command.Parameters.AddWithValue("$hash", tokenHash);
            command.Parameters.AddWithValue("$user", userId);
            command.Parameters.AddWithValue("$now", Now());
            command.Parameters.AddWithValue("$expires", expiresAt.ToString("O"));
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Пользователь по токену, если токен не истёк и пользователь не заблокирован.
        /// Срок токена продлевается при использовании (не чаще раза в час).
        /// </summary>
        public UserRecord FindUserByToken(string tokenHash, TimeSpan lifetime)
        {
            using var connection = Open();
            string expiresAt;
            UserRecord user;
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT u.id, u.login, u.password_hash, u.is_admin, u.disabled, t.expires_at " +
                    "FROM tokens t JOIN users u ON u.id = t.user_id WHERE t.token_hash = $hash";
                command.Parameters.AddWithValue("$hash", tokenHash);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                user = ReadUser(reader);
                expiresAt = reader.GetString(5);
            }

            var expires = DateTime.Parse(expiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (user.Disabled || expires < DateTime.UtcNow)
            {
                return null;
            }

            var renewed = DateTime.UtcNow + lifetime;
            if (renewed - expires > TimeSpan.FromHours(1))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE tokens SET expires_at = $expires WHERE token_hash = $hash";
                command.Parameters.AddWithValue("$expires", renewed.ToString("O"));
                command.Parameters.AddWithValue("$hash", tokenHash);
                command.ExecuteNonQuery();
            }

            return user;
        }

        public void DeleteToken(string tokenHash)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM tokens WHERE token_hash = $hash OR expires_at < $now";
            command.Parameters.AddWithValue("$hash", tokenHash);
            command.Parameters.AddWithValue("$now", Now());
            command.ExecuteNonQuery();
        }

        // ---------- Хранилище ----------

        public VaultRecord GetVault()
        {
            using var connection = Open();
            var salt = GetMeta(connection, "vault_salt");
            var check = GetMeta(connection, "vault_check");
            if (salt == null || check == null)
            {
                return null;
            }

            return new VaultRecord(
                Convert.FromBase64String(salt),
                int.Parse(GetMeta(connection, "vault_iterations") ?? "600000"),
                Convert.FromBase64String(check));
        }

        /// <summary>Сохраняет параметры хранилища, только если их ещё нет (первая настройка).</summary>
        public bool InitVault(VaultRecord vault)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            if (GetMeta(connection, "vault_check", transaction) != null)
            {
                return false;
            }

            SetMeta(connection, transaction, "vault_salt", Convert.ToBase64String(vault.Salt));
            SetMeta(connection, transaction, "vault_iterations", vault.Iterations.ToString());
            SetMeta(connection, transaction, "vault_check", Convert.ToBase64String(vault.Check));
            transaction.Commit();
            return true;
        }

        /// <summary>Полный сброс: удаляет все записи и параметры хранилища (если фраза утеряна).</summary>
        public void ResetVault()
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction,
                "DELETE FROM items; DELETE FROM meta WHERE key IN ('vault_salt', 'vault_iterations', 'vault_check'); " +
                "UPDATE meta SET value = CAST(value AS INTEGER) + 1 WHERE key = 'seq';");
            transaction.Commit();
        }

        public (long Seq, List<ItemRecord> Items) GetItemsSince(long since)
        {
            var items = new List<ItemRecord>();
            using var connection = Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            var seq = long.Parse(GetMeta(connection, "seq", transaction) ?? "0");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "SELECT id, version, deleted, data, seq, updated_at, updated_by FROM items WHERE seq > $since ORDER BY seq";
            command.Parameters.AddWithValue("$since", since);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(ReadItem(reader));
            }

            return (seq, items);
        }

        public ItemRecord GetItem(string id)
        {
            using var connection = Open();
            return GetItem(connection, null, id);
        }

        /// <summary>
        /// Создаёт или обновляет запись, если её версия на сервере равна <paramref name="baseVersion"/>
        /// (0 — записи ещё нет). Иначе — конфликт, и клиент получает текущую версию.
        /// </summary>
        public (WriteOutcome Outcome, ItemRecord Item) PutItem(string id, byte[] data, long baseVersion, string login)
            => Write(id, baseVersion, login, data, deleted: false);

        /// <summary>Удаляет запись (оставляет «надгробие», чтобы удаление дошло до других клиентов).</summary>
        public (WriteOutcome Outcome, ItemRecord Item) DeleteItem(string id, long baseVersion, string login)
            => Write(id, baseVersion, login, null, deleted: true);

        private (WriteOutcome, ItemRecord) Write(string id, long baseVersion, string login, byte[] data, bool deleted)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var current = GetItem(connection, transaction, id);
            var currentVersion = current?.Version ?? 0;
            if (currentVersion != baseVersion)
            {
                return (WriteOutcome.Conflict, current);
            }

            if (deleted && (current == null || current.Deleted))
            {
                return (WriteOutcome.Ok, current);
            }

            var seq = long.Parse(GetMeta(connection, "seq", transaction) ?? "0") + 1;
            SetMeta(connection, transaction, "seq", seq.ToString());

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO items (id, version, deleted, data, seq, updated_at, updated_by) " +
                    "VALUES ($id, $version, $deleted, $data, $seq, $now, $by) " +
                    "ON CONFLICT(id) DO UPDATE SET version = excluded.version, deleted = excluded.deleted, " +
                    "data = excluded.data, seq = excluded.seq, updated_at = excluded.updated_at, updated_by = excluded.updated_by";
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$version", currentVersion + 1);
                command.Parameters.AddWithValue("$deleted", deleted ? 1 : 0);
                command.Parameters.AddWithValue("$data", (object)data ?? DBNull.Value);
                command.Parameters.AddWithValue("$seq", seq);
                command.Parameters.AddWithValue("$now", Now());
                command.Parameters.AddWithValue("$by", login);
                command.ExecuteNonQuery();
            }

            var written = GetItem(connection, transaction, id);
            transaction.Commit();
            return (WriteOutcome.Ok, written);
        }

        // ---------- Служебное ----------

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            Execute(connection, null, "PRAGMA foreign_keys = ON;");
            return connection;
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string GetMeta(SqliteConnection connection, string key, SqliteTransaction transaction = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT value FROM meta WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }

        private static void SetMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        private static ItemRecord GetItem(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id, version, deleted, data, seq, updated_at, updated_by FROM items WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadItem(reader) : null;
        }

        private static ItemRecord ReadItem(SqliteDataReader reader) => new ItemRecord(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetInt64(2) != 0,
            reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3),
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetString(6));

        private static UserRecord ReadUser(SqliteDataReader reader) => new UserRecord(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0, reader.GetInt64(4) != 0);

        private static string Now() => DateTime.UtcNow.ToString("O");
    }
}
