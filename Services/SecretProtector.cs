using System;
using System.Security.Cryptography;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>
    /// Хранение секретов (ключа шифрования, токена сервера) в settings.json.
    /// Они защищаются DPAPI текущего пользователя Windows: скопированный на другой компьютер
    /// или другому пользователю settings.json ничего не раскроет.
    /// </summary>
    public static class SecretProtector
    {
        private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("RemoteAccessAddressBook");

        public static string Protect(byte[] secret)
        {
            if (secret == null || secret.Length == 0)
            {
                return string.Empty;
            }

            return Convert.ToBase64String(ProtectedData.Protect(secret, Entropy, DataProtectionScope.CurrentUser));
        }

        /// <summary>null — секрета нет или он защищён другим пользователем/компьютером.</summary>
        public static byte[] Unprotect(string protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue))
            {
                return null;
            }

            try
            {
                return ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string ProtectString(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : Protect(System.Text.Encoding.UTF8.GetBytes(value));

        public static string UnprotectString(string protectedValue)
        {
            var bytes = Unprotect(protectedValue);
            return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
