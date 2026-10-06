using System;
using System.Security.Cryptography;
using System.Text;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>
    /// Шифрование контактов ключом, выведенным из парольной фразы.
    /// Ключ: PBKDF2-HMAC-SHA256 (соль 16 байт, 600 000 итераций) → 32 байта.
    /// Шифр: AES-256-GCM, формат блоба: [1 байт версии][12 байт nonce][шифротекст][16 байт тега].
    /// Дополнительные данные (AAD) привязывают шифротекст к записи, чтобы его нельзя было
    /// подставить в чужую запись.
    /// </summary>
    public static class VaultCrypto
    {
        public const int DefaultIterations = 600_000;

        /// <summary>Префикс зашифрованного значения в текстовом поле локальной базы.</summary>
        public const string TextPrefix = "enc:v1:";

        private const byte FormatVersion = 1;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;

        /// <summary>Известный текст, по которому проверяется правильность парольной фразы.</summary>
        private static readonly byte[] CheckPlaintext = Encoding.UTF8.GetBytes("RemoteAccessAddressBook vault check");

        public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(16);

        public static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
        {
            if (string.IsNullOrEmpty(passphrase))
            {
                throw new ArgumentException("Пустая парольная фраза.", nameof(passphrase));
            }

            return Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, KeySize);
        }

        public static byte[] Encrypt(byte[] key, byte[] plaintext, string associatedData)
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, Aad(associatedData));
            }

            var blob = new byte[1 + NonceSize + ciphertext.Length + TagSize];
            blob[0] = FormatVersion;
            Buffer.BlockCopy(nonce, 0, blob, 1, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, blob, 1 + NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, blob, 1 + NonceSize + ciphertext.Length, TagSize);
            return blob;
        }

        /// <summary>Расшифровывает блоб. Неверный ключ или подмена данных — CryptographicException.</summary>
        public static byte[] Decrypt(byte[] key, byte[] blob, string associatedData)
        {
            if (blob == null || blob.Length < 1 + NonceSize + TagSize || blob[0] != FormatVersion)
            {
                throw new CryptographicException("Неизвестный формат зашифрованных данных.");
            }

            var length = blob.Length - 1 - NonceSize - TagSize;
            var plaintext = new byte[length];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(
                    blob.AsSpan(1, NonceSize),
                    blob.AsSpan(1 + NonceSize, length),
                    blob.AsSpan(1 + NonceSize + length, TagSize),
                    plaintext,
                    Aad(associatedData));
            }

            return plaintext;
        }

        /// <summary>Проверочный блоб: сохраняется рядом с солью, чтобы проверять фразу при вводе.</summary>
        public static byte[] CreateCheck(byte[] key) => Encrypt(key, CheckPlaintext, "vault-check");

        public static bool VerifyCheck(byte[] key, byte[] check)
        {
            try
            {
                var plaintext = Decrypt(key, check, "vault-check");
                return CryptographicOperations.FixedTimeEquals(plaintext, CheckPlaintext);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>Шифрует строку для хранения в текстовом поле: "enc:v1:" + base64.</summary>
        public static string EncryptText(byte[] key, string value, string associatedData)
        {
            var blob = Encrypt(key, Encoding.UTF8.GetBytes(value ?? string.Empty), associatedData);
            return TextPrefix + Convert.ToBase64String(blob);
        }

        /// <summary>
        /// Расшифровывает значение поля. Значение без префикса возвращается как есть:
        /// его могла записать старая версия программы, которая о шифровании не знает.
        /// </summary>
        public static string DecryptText(byte[] key, string value, string associatedData)
        {
            if (!IsEncryptedText(value))
            {
                return value ?? string.Empty;
            }

            if (key == null)
            {
                throw new CryptographicException("База зашифрована, а ключ не задан.");
            }

            var blob = Convert.FromBase64String(value.Substring(TextPrefix.Length));
            return Encoding.UTF8.GetString(Decrypt(key, blob, associatedData));
        }

        public static bool IsEncryptedText(string value) =>
            value != null && value.StartsWith(TextPrefix, StringComparison.Ordinal);

        private static byte[] Aad(string associatedData) =>
            Encoding.UTF8.GetBytes(associatedData ?? string.Empty);
    }
}
