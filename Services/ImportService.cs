using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using RemoteAccessAddressBook.Data;
using RemoteAccessAddressBook.Models;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>Поля контакта, доступные для маппинга при импорте.</summary>
    public enum ContactField
    {
        None,
        Name,
        Comment,
        AnyDesk,
        Rudesktop,
        Assistant,
        Ammyy,
        Rdp,
        Password,
        RdpLogin,
        RdpPassword,
    }

    /// <summary>Прочитанная таблица из файла CSV/XLSX.</summary>
    public class ImportTable
    {
        public List<string> Headers { get; } = new List<string>();

        public List<string[]> Rows { get; } = new List<string[]>();
    }

    /// <summary>Отчёт об импорте.</summary>
    public class ImportReport
    {
        public int Added { get; set; }

        public int Duplicates { get; set; }

        public int Errors { get; set; }

        public List<string> ErrorMessages { get; } = new List<string>();
    }

    /// <summary>Импорт контактов из CSV и XLSX.</summary>
    public static class ImportService
    {
        /// <summary>Поля, которые НЕ участвуют в сравнении при поиске дубликатов.</summary>
        private static readonly HashSet<ContactField> PasswordFields = new HashSet<ContactField>
        {
            ContactField.Password,
            ContactField.RdpPassword,
        };

        public static string DisplayName(ContactField field)
        {
            switch (field)
            {
                case ContactField.Name: return "Имя";
                case ContactField.Comment: return "Комментарий";
                case ContactField.AnyDesk: return "AnyDesk";
                case ContactField.Rudesktop: return "Rudesktop";
                case ContactField.Assistant: return "Ассистент";
                case ContactField.Ammyy: return "AmmyyAdmin";
                case ContactField.Rdp: return "RDP";
                case ContactField.Password: return "Пароль";
                case ContactField.RdpLogin: return "RDP Логин";
                case ContactField.RdpPassword: return "RDP Пароль";
                default: return "— не импортировать —";
            }
        }

        /// <summary>Читает таблицу из файла. Первая строка считается заголовком.</summary>
        public static ImportTable ReadTable(string path, char csvDelimiter)
        {
            var extension = Path.GetExtension(path);
            if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
            {
                return ReadXlsx(path);
            }

            return ReadCsv(path, csvDelimiter);
        }

        /// <summary>Пытается угадать разделитель CSV по первой строке файла.</summary>
        public static char DetectDelimiter(string path)
        {
            try
            {
                using var reader = new StreamReader(path, Encoding.UTF8, true);
                var line = reader.ReadLine() ?? string.Empty;
                var semicolons = CountOutsideQuotes(line, ';');
                var commas = CountOutsideQuotes(line, ',');
                var tabs = CountOutsideQuotes(line, '\t');
                if (tabs > semicolons && tabs > commas)
                {
                    return '\t';
                }

                return commas > semicolons ? ',' : ';';
            }
            catch (Exception)
            {
                return ';';
            }
        }

        /// <summary>
        /// Импортирует строки таблицы. Дубликаты (совпадение по всем импортируемым
        /// полям, кроме паролей) пропускаются.
        /// </summary>
        public static ImportReport Import(
            ImportTable table,
            IDictionary<ContactField, int> mapping,
            long? groupId,
            IList<Contact> existingContacts,
            Database database)
        {
            var report = new ImportReport();
            if (table == null || mapping == null || database == null)
            {
                return report;
            }

            var comparedFields = new List<ContactField>();
            foreach (var pair in mapping)
            {
                if (pair.Key != ContactField.None && pair.Value >= 0 && !PasswordFields.Contains(pair.Key))
                {
                    comparedFields.Add(pair.Key);
                }
            }

            var known = new List<Contact>(existingContacts ?? new List<Contact>());

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                try
                {
                    var contact = new Contact { GroupId = groupId };
                    var hasAnyValue = false;

                    foreach (var pair in mapping)
                    {
                        if (pair.Key == ContactField.None || pair.Value < 0 || pair.Value >= row.Length)
                        {
                            continue;
                        }

                        var value = (row[pair.Value] ?? string.Empty).Trim();
                        SetField(contact, pair.Key, value);
                        if (!string.IsNullOrEmpty(value))
                        {
                            hasAnyValue = true;
                        }
                    }

                    if (!hasAnyValue)
                    {
                        // Полностью пустая строка — просто пропускаем.
                        continue;
                    }

                    if (IsDuplicate(contact, known, comparedFields))
                    {
                        report.Duplicates++;
                        continue;
                    }

                    database.InsertContact(contact);
                    known.Add(contact);
                    report.Added++;
                }
                catch (Exception ex)
                {
                    report.Errors++;
                    if (report.ErrorMessages.Count < 20)
                    {
                        report.ErrorMessages.Add("Строка " + (rowIndex + 2) + ": " + ex.Message);
                    }
                }
            }

            return report;
        }

        private static bool IsDuplicate(Contact candidate, IList<Contact> known, IList<ContactField> comparedFields)
        {
            if (comparedFields.Count == 0)
            {
                return false;
            }

            foreach (var existing in known)
            {
                var same = true;
                foreach (var field in comparedFields)
                {
                    if (!string.Equals(
                            GetField(existing, field) ?? string.Empty,
                            GetField(candidate, field) ?? string.Empty,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetField(Contact contact, ContactField field, string value)
        {
            switch (field)
            {
                case ContactField.Name: contact.Name = value; break;
                case ContactField.Comment: contact.Comment = value; break;
                case ContactField.AnyDesk: contact.AnyDesk = value; break;
                case ContactField.Rudesktop: contact.Rudesktop = value; break;
                case ContactField.Assistant: contact.Assistant = value; break;
                case ContactField.Ammyy: contact.Ammyy = value; break;
                case ContactField.Rdp: contact.Rdp = value; break;
                case ContactField.Password: contact.Password = value; break;
                case ContactField.RdpLogin: contact.RdpLogin = value; break;
                case ContactField.RdpPassword: contact.RdpPassword = value; break;
            }
        }

        private static string GetField(Contact contact, ContactField field)
        {
            switch (field)
            {
                case ContactField.Name: return contact.Name;
                case ContactField.Comment: return contact.Comment;
                case ContactField.AnyDesk: return contact.AnyDesk;
                case ContactField.Rudesktop: return contact.Rudesktop;
                case ContactField.Assistant: return contact.Assistant;
                case ContactField.Ammyy: return contact.Ammyy;
                case ContactField.Rdp: return contact.Rdp;
                case ContactField.Password: return contact.Password;
                case ContactField.RdpLogin: return contact.RdpLogin;
                case ContactField.RdpPassword: return contact.RdpPassword;
                default: return string.Empty;
            }
        }

        private static ImportTable ReadXlsx(string path)
        {
            var table = new ImportTable();
            using var workbook = new XLWorkbook(path);
            var worksheet = workbook.Worksheet(1);
            var range = worksheet.RangeUsed();
            if (range == null)
            {
                return table;
            }

            var columnCount = range.ColumnCount();
            var first = true;
            foreach (var row in range.Rows())
            {
                var values = new string[columnCount];
                for (var i = 0; i < columnCount; i++)
                {
                    values[i] = row.Cell(i + 1).GetFormattedString() ?? string.Empty;
                }

                if (first)
                {
                    first = false;
                    for (var i = 0; i < columnCount; i++)
                    {
                        var header = values[i];
                        table.Headers.Add(string.IsNullOrWhiteSpace(header) ? "Колонка " + (i + 1) : header);
                    }

                    continue;
                }

                table.Rows.Add(values);
            }

            return table;
        }

        private static ImportTable ReadCsv(string path, char delimiter)
        {
            var table = new ImportTable();
            var lines = SplitRecords(File.ReadAllText(path, Encoding.UTF8));
            var first = true;
            foreach (var line in lines)
            {
                var values = ParseCsvLine(line, delimiter);
                if (first)
                {
                    first = false;
                    for (var i = 0; i < values.Length; i++)
                    {
                        var header = values[i];
                        table.Headers.Add(string.IsNullOrWhiteSpace(header) ? "Колонка " + (i + 1) : header);
                    }

                    continue;
                }

                table.Rows.Add(values);
            }

            return table;
        }

        /// <summary>Разбивает текст на записи CSV с учётом переводов строк внутри кавычек.</summary>
        private static List<string> SplitRecords(string text)
        {
            var records = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return records;
            }

            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text.Substring(1);
            }

            var builder = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    builder.Append(c);
                    continue;
                }

                if (!inQuotes && (c == '\n' || c == '\r'))
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    records.Add(builder.ToString());
                    builder.Clear();
                    continue;
                }

                builder.Append(c);
            }

            if (builder.Length > 0)
            {
                records.Add(builder.ToString());
            }

            records.RemoveAll(string.IsNullOrWhiteSpace);
            return records;
        }

        private static string[] ParseCsvLine(string line, char delimiter)
        {
            var values = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            builder.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    values.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(c);
                }
            }

            values.Add(builder.ToString());
            return values.ToArray();
        }

        private static int CountOutsideQuotes(string line, char symbol)
        {
            var count = 0;
            var inQuotes = false;
            foreach (var c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && c == symbol)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
