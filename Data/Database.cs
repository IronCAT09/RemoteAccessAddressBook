using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using RemouteAddressBook.Models;

namespace RemouteAddressBook.Data
{
    /// <summary>Доступ к базе данных SQLite.</summary>
    public class Database
    {
        private readonly string _connectionString;

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
            }.ToString();
        }

        public string DatabasePath { get; }

        /// <summary>Создаёт файл БД и схему, если их ещё нет.</summary>
        public void EnsureCreated()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
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
";
            command.ExecuteNonQuery();
        }

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

        public List<Contact> LoadContacts()
        {
            var contacts = new List<Contact>();
            var byId = new Dictionary<long, Contact>();
            using var connection = OpenConnection();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT id, name, comment, anydesk, rudesktop, assistant, ammyy, rdp, password, " +
                    "rdp_login, rdp_password, group_id FROM contacts ORDER BY name COLLATE NOCASE";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var contact = new Contact
                    {
                        Id = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        Comment = reader.GetString(2),
                        AnyDesk = reader.GetString(3),
                        Rudesktop = reader.GetString(4),
                        Assistant = reader.GetString(5),
                        Ammyy = reader.GetString(6),
                        Rdp = reader.GetString(7),
                        Password = reader.GetString(8),
                        RdpLogin = reader.GetString(9),
                        RdpPassword = reader.GetString(10),
                        GroupId = reader.IsDBNull(11) ? (long?)null : reader.GetInt64(11),
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
                        contact.LabelIds.Add(reader.GetInt64(1));
                    }
                }
            }

            return contacts;
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
            return id;
        }

        public void UpdateContact(Contact contact)
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
                    "group_id = $groupId WHERE id = $id";
                BindContact(command, contact);
                command.Parameters.AddWithValue("$id", contact.Id);
                command.ExecuteNonQuery();
            }

            ReplaceLabels(connection, transaction, contact.Id, contact.LabelIds);
            transaction.Commit();
        }

        public void DeleteContact(long contactId)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM contacts WHERE id = $id";
            command.Parameters.AddWithValue("$id", contactId);
            command.ExecuteNonQuery();
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
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO settings (key, value) VALUES ($key, $value) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON;";
                pragma.ExecuteNonQuery();
            }

            return connection;
        }

        private static void BindContact(SqliteCommand command, Contact contact)
        {
            command.Parameters.AddWithValue("$name", contact.Name ?? string.Empty);
            command.Parameters.AddWithValue("$comment", contact.Comment ?? string.Empty);
            command.Parameters.AddWithValue("$anydesk", contact.AnyDesk ?? string.Empty);
            command.Parameters.AddWithValue("$rudesktop", contact.Rudesktop ?? string.Empty);
            command.Parameters.AddWithValue("$assistant", contact.Assistant ?? string.Empty);
            command.Parameters.AddWithValue("$ammyy", contact.Ammyy ?? string.Empty);
            command.Parameters.AddWithValue("$rdp", contact.Rdp ?? string.Empty);
            command.Parameters.AddWithValue("$password", contact.Password ?? string.Empty);
            command.Parameters.AddWithValue("$rdpLogin", contact.RdpLogin ?? string.Empty);
            command.Parameters.AddWithValue("$rdpPassword", contact.RdpPassword ?? string.Empty);
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
