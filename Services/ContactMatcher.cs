using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RemoteAccessAddressBook.Models;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>
    /// Сравнение контактов из разных баз.
    /// Дубль — совпадают все поля контакта (тогда локальная копия скрывается и при синхронизации
    /// не копируется). Группа и метки не сравниваются: их переименование в одной базе не должно
    /// превращать одинаковые контакты в расхождения.
    /// «Тот же контакт» — совпадает хотя бы один ID подключения, а если ID нет — имя;
    /// если при этом данные различаются, это расхождение, которое решает пользователь.
    /// </summary>
    public static class ContactMatcher
    {
        /// <summary>Отпечаток полей контакта (без группы и меток): равны отпечатки — контакт тот же.</summary>
        public static string Fingerprint(Contact contact)
        {
            var parts = new[]
            {
                contact.Name, contact.Comment, contact.AnyDesk, contact.Rudesktop, contact.Assistant, contact.Ammyy,
                contact.Rdp, contact.Password, contact.RdpLogin, contact.RdpPassword,
            };

            var builder = new StringBuilder();
            foreach (var part in parts)
            {
                builder.Append((part ?? string.Empty).Trim()).Append('\u001e');
            }

            return builder.ToString();
        }

        /// <summary>Ключи, по которым контакт ищется в другой базе.</summary>
        public static IEnumerable<string> MatchKeys(Contact contact)
        {
            var hasId = false;
            foreach (var key in ToolKeys.All)
            {
                var id = NormalizeId(contact.GetToolId(key));
                if (id.Length > 0)
                {
                    hasId = true;
                    yield return key + ":" + id;
                }
            }

            if (!hasId && !string.IsNullOrWhiteSpace(contact.Name))
            {
                yield return "name:" + contact.Name.Trim().ToLowerInvariant();
            }
        }

        /// <summary>Тот же контакт в другой базе (совпал ключ), без учёта того, равны ли данные.</summary>
        public static bool IsSameContact(Contact a, Contact b) => MatchKeys(a).Intersect(MatchKeys(b)).Any();

        /// <summary>ID без пробелов и дефисов: «123 456 789» и «123-456-789» — один и тот же.</summary>
        private static string NormalizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                if (ch != ' ' && ch != '-' && ch != ' ')
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString();
        }
    }
}
