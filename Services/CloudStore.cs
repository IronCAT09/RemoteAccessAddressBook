using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RemoteAccessAddressBook.Models;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>Состояние подключения к облачной базе.</summary>
    public enum CloudState
    {
        /// <summary>Адрес сервера не задан — облако не используется.</summary>
        Off,

        /// <summary>Нужно войти (нет токена или он истёк).</summary>
        NeedsLogin,

        /// <summary>Нужна парольная фраза (ключа нет или хранилище сброшено).</summary>
        NeedsPassphrase,

        /// <summary>Сервер недоступен — показываем контакты из локального кэша.</summary>
        Offline,

        Ready,
    }

    /// <summary>Ответ на попытку записи в облако.</summary>
    public class CloudSaveResult
    {
        public Data.SaveResult Result { get; set; }

        /// <summary>При конфликте — актуальная версия контакта с сервера (null, если его удалили).</summary>
        public Contact Current { get; set; }
    }

    /// <summary>
    /// Облачная база глазами приложения: кэш зашифрованных записей (на диске, чтобы контакты были
    /// видны и без связи), расшифровка, получение изменений с сервера и запись с проверкой версии.
    /// Сервер видит только шифротекст; ключ — из парольной фразы команды.
    /// </summary>
    public class CloudStore
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly CloudSettings _settings;
        private readonly Dictionary<string, CloudItem> _items = new Dictionary<string, CloudItem>(StringComparer.OrdinalIgnoreCase);
        private byte[] _key;
        private long _seq;
        private string _vaultId;
        private List<Contact> _contacts = new List<Contact>();

        public CloudStore(CloudSettings settings)
        {
            _settings = settings;
            if (!settings.IsConfigured)
            {
                State = CloudState.Off;
                return;
            }

            _key = SecretProtector.Unprotect(settings.ProtectedKey);
            LoadCache();
            State = string.IsNullOrEmpty(Token) ? CloudState.NeedsLogin
                : _key == null ? CloudState.NeedsPassphrase
                : CloudState.Offline;
        }

        public CloudState State { get; private set; }

        /// <summary>Текст последней ошибки связи (для строки статуса).</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>Сколько записей не удалось расшифровать (другая парольная фраза или повреждение).</summary>
        public int UndecryptableCount { get; private set; }

        /// <summary>Можно ли писать в облако прямо сейчас.</summary>
        public bool CanWrite => State == CloudState.Ready;

        /// <summary>Контакты облачной базы (расшифрованные), по имени.</summary>
        public IReadOnlyList<Contact> Contacts => _contacts;

        private string Token => SecretProtector.UnprotectString(_settings.ProtectedToken);

        /// <summary>
        /// Забирает изменения с сервера. true — контакты изменились.
        /// Ошибки связи не выбрасываются: состояние переходит в Offline, кэш остаётся.
        /// </summary>
        public async Task<bool> PollAsync()
        {
            if (State == CloudState.Off || State == CloudState.NeedsLogin || State == CloudState.NeedsPassphrase)
            {
                return false;
            }

            try
            {
                using var client = new CloudClient(_settings.ServerUrl, Token);
                var page = await client.GetItemsAsync(_seq);

                var changed = false;
                if (_vaultId != null && page.VaultId != _vaultId)
                {
                    // Хранилище на сервере сбросили и задали новую фразу: старый кэш и ключ недействительны.
                    ResetLocal(forgetKey: true);
                    State = CloudState.NeedsPassphrase;
                    LastError = "Облачную базу сбросили на сервере — введите новую парольную фразу.";
                    return true;
                }

                _vaultId = page.VaultId;
                foreach (var item in page.Items)
                {
                    _items[item.Id] = item;
                    changed = true;
                }

                if (page.Seq != _seq)
                {
                    _seq = page.Seq;
                    SaveCache();
                }

                if (changed || State != CloudState.Ready)
                {
                    Decode();
                    changed = true;
                }

                State = CloudState.Ready;
                LastError = string.Empty;
                return changed;
            }
            catch (CloudAuthException ex)
            {
                State = CloudState.NeedsLogin;
                LastError = ex.Message;
                return true;
            }
            catch (CloudException ex)
            {
                // Сообщаем об изменении и при смене текста ошибки: после запуска без связи
                // состояние уже Offline, а причину в подсказке показать нужно.
                var changed = State == CloudState.Ready || LastError != ex.Message;
                State = CloudState.Offline;
                LastError = ex.Message;
                return changed;
            }
        }

        /// <summary>Создаёт (пустой CloudId) или обновляет контакт в облаке.</summary>
        public async Task<CloudSaveResult> SaveAsync(Contact contact, bool force = false)
        {
            EnsureWritable();
            var isNew = string.IsNullOrEmpty(contact.CloudId);
            var id = isNew ? Guid.NewGuid().ToString() : contact.CloudId;
            long baseVersion = isNew ? 0 : contact.Version;

            var data = Encrypt(id, contact);
            CloudWriteResult result;
            try
            {
                using var client = new CloudClient(_settings.ServerUrl, Token);
                result = await client.PutItemAsync(id, data, baseVersion);
                if (result.Conflict && force && result.Current != null)
                {
                    // «Сохранить поверх»: повторяем на актуальной версии.
                    result = await client.PutItemAsync(id, data, result.Current.Version);
                }
            }
            catch (Exception ex) when (TrackFailure(ex))
            {
                throw;
            }

            MarkOnline();

            if (result.Conflict)
            {
                if (result.Current != null)
                {
                    _items[id] = result.Current;
                    Decode();
                }

                var current = result.Current == null || result.Current.Deleted ? null : _contacts.FirstOrDefault(c => c.CloudId == id);
                return new CloudSaveResult
                {
                    Result = current == null ? Data.SaveResult.NotFound : Data.SaveResult.Conflict,
                    Current = current,
                };
            }

            _items[id] = result.Item;
            contact.CloudId = id;
            contact.Source = ContactSource.Cloud;
            contact.Version = result.Item.Version;
            Decode();
            SaveCache();
            return new CloudSaveResult { Result = Data.SaveResult.Saved };
        }

        /// <summary>Удаляет контакт из облака. false — его изменили на другом компьютере.</summary>
        public async Task<bool> DeleteAsync(Contact contact, bool force = false)
        {
            EnsureWritable();
            CloudWriteResult result;
            try
            {
                using var client = new CloudClient(_settings.ServerUrl, Token);
                result = await client.DeleteItemAsync(contact.CloudId, contact.Version);
                if (result.Conflict && force && result.Current != null)
                {
                    result = await client.DeleteItemAsync(contact.CloudId, result.Current.Version);
                }
            }
            catch (Exception ex) when (TrackFailure(ex))
            {
                throw;
            }

            MarkOnline();

            if (result.Conflict)
            {
                if (result.Current != null)
                {
                    _items[contact.CloudId] = result.Current;
                    Decode();
                }

                return false;
            }

            _items.Remove(contact.CloudId);
            Decode();
            SaveCache();
            return true;
        }

        // ---------- Подключение (вызывается из окна входа) ----------

        /// <summary>Сохраняет результат входа: токен защищается DPAPI.</summary>
        public static void StoreLogin(CloudSettings settings, string serverUrl, CloudLoginResult login)
        {
            if (!string.Equals(settings.ServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(settings.Login, login.Login, StringComparison.OrdinalIgnoreCase))
            {
                // Другой сервер или пользователь — старый ключ к нему не относится.
                settings.ProtectedKey = string.Empty;
            }

            settings.ServerUrl = serverUrl;
            settings.Login = login.Login;
            settings.IsAdmin = login.IsAdmin;
            settings.ProtectedToken = SecretProtector.ProtectString(login.Token);
        }

        public static void StoreKey(CloudSettings settings, byte[] key) =>
            settings.ProtectedKey = SecretProtector.Protect(key);

        /// <summary>
        /// Удаляет локальный кэш облака. Вызывается и при подключении: кэш мог остаться от хранилища,
        /// которое с тех пор сбросили, и тогда его чужой идентификатор выглядел бы как новый сброс
        /// и стирал только что полученный ключ.
        /// </summary>
        public static void DeleteCache(CloudSettings settings)
        {
            var cache = CachePath(settings);
            if (cache != null && File.Exists(cache))
            {
                File.Delete(cache);
            }
        }

        /// <summary>Отключение: токен отзывается на сервере, локальный кэш удаляется.</summary>
        public static async Task DisconnectAsync(CloudSettings settings)
        {
            var token = SecretProtector.UnprotectString(settings.ProtectedToken);
            if (settings.IsConfigured && !string.IsNullOrEmpty(token))
            {
                using var client = new CloudClient(settings.ServerUrl, token);
                await client.LogoutAsync();
            }

            DeleteCache(settings);

            settings.ServerUrl = string.Empty;
            settings.Login = string.Empty;
            settings.IsAdmin = false;
            settings.ProtectedToken = string.Empty;
            settings.ProtectedKey = string.Empty;
        }

        // ---------- Шифрование записей ----------

        private byte[] Encrypt(string id, Contact contact)
        {
            var payload = new CloudContactPayload
            {
                Name = contact.Name,
                Comment = contact.Comment,
                AnyDesk = contact.AnyDesk,
                Rudesktop = contact.Rudesktop,
                Assistant = contact.Assistant,
                Ammyy = contact.Ammyy,
                Rdp = contact.Rdp,
                Password = contact.Password,
                RdpLogin = contact.RdpLogin,
                RdpPassword = contact.RdpPassword,
                Group = contact.GroupName,
                Labels = contact.LabelNames.OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase).ToList(),
            };
            return VaultCrypto.Encrypt(_key, JsonSerializer.SerializeToUtf8Bytes(payload, Json), "item:" + id.ToLowerInvariant());
        }

        /// <summary>Пересобирает список контактов из кэша записей.</summary>
        private void Decode()
        {
            var contacts = new List<Contact>();
            var failed = 0;
            foreach (var item in _items.Values)
            {
                if (item.Deleted || string.IsNullOrEmpty(item.Data) || _key == null)
                {
                    continue;
                }

                try
                {
                    var plaintext = VaultCrypto.Decrypt(_key, Convert.FromBase64String(item.Data), "item:" + item.Id.ToLowerInvariant());
                    var payload = JsonSerializer.Deserialize<CloudContactPayload>(plaintext, Json);
                    var contact = new Contact
                    {
                        Source = ContactSource.Cloud,
                        CloudId = item.Id.ToLowerInvariant(),
                        Version = item.Version,
                        Name = payload.Name ?? string.Empty,
                        Comment = payload.Comment ?? string.Empty,
                        AnyDesk = payload.AnyDesk ?? string.Empty,
                        Rudesktop = payload.Rudesktop ?? string.Empty,
                        Assistant = payload.Assistant ?? string.Empty,
                        Ammyy = payload.Ammyy ?? string.Empty,
                        Rdp = payload.Rdp ?? string.Empty,
                        Password = payload.Password ?? string.Empty,
                        RdpLogin = payload.RdpLogin ?? string.Empty,
                        RdpPassword = payload.RdpPassword ?? string.Empty,
                        GroupName = payload.Group ?? string.Empty,
                    };
                    if (payload.Labels != null)
                    {
                        contact.LabelNames.UnionWith(payload.Labels.Where(l => !string.IsNullOrWhiteSpace(l)));
                    }

                    contacts.Add(contact);
                }
                catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is JsonException)
                {
                    failed++;
                }
            }

            contacts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            _contacts = contacts;
            UndecryptableCount = failed;
        }

        /// <summary>
        /// Проверка перед записью. Без связи запись всё равно пробуем: сервер мог вернуться
        /// раньше очередного опроса (например, пока было открыто окно редактирования).
        /// </summary>
        private void EnsureWritable()
        {
            if (CanWrite || State == CloudState.Offline)
            {
                return;
            }

            throw new CloudException(State switch
            {
                CloudState.NeedsLogin => "Нужно войти на сервер облачной базы (кнопка облака → «Подключиться…»).",
                CloudState.NeedsPassphrase => "Нужна парольная фраза облачной базы (кнопка облака → «Подключиться…»).",
                _ => "Облачная база не подключена.",
            });
        }

        /// <summary>Обновляет состояние по результату запроса записи; исключение не перехватывает.</summary>
        private bool TrackFailure(Exception ex)
        {
            if (ex is CloudAuthException)
            {
                State = CloudState.NeedsLogin;
                LastError = ex.Message;
            }
            else if (ex is CloudException)
            {
                State = CloudState.Offline;
                LastError = ex.Message;
            }

            return false;
        }

        private void MarkOnline()
        {
            if (State == CloudState.Offline)
            {
                State = CloudState.Ready;
                LastError = string.Empty;
            }
        }

        // ---------- Кэш на диске ----------

        /// <summary>
        /// Кэш — записи как они лежат на сервере (шифротекст), поэтому хранить его на диске безопасно.
        /// Файл свой для каждого сервера и пользователя.
        /// </summary>
        private static string CachePath(CloudSettings settings)
        {
            if (!settings.IsConfigured)
            {
                return null;
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                settings.ServerUrl.ToLowerInvariant() + "|" + (settings.Login ?? string.Empty).ToLowerInvariant()))).Substring(0, 16);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RemoteAccessAddressBook", "cloud-" + hash + ".json");
        }

        private void LoadCache()
        {
            try
            {
                var path = CachePath(_settings);
                if (path == null || !File.Exists(path))
                {
                    return;
                }

                var cache = JsonSerializer.Deserialize<CloudCache>(File.ReadAllBytes(path), Json);
                if (cache?.Items == null)
                {
                    return;
                }

                _seq = cache.Seq;
                _vaultId = cache.VaultId;
                foreach (var item in cache.Items)
                {
                    _items[item.Id] = item;
                }

                Decode();
            }
            catch (Exception)
            {
                // Повреждённый кэш — просто перекачаем всё с сервера.
                ResetLocal(forgetKey: false);
            }
        }

        private void SaveCache()
        {
            try
            {
                var path = CachePath(_settings);
                if (path == null)
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var cache = new CloudCache { Seq = _seq, VaultId = _vaultId, Items = _items.Values.Where(i => !i.Deleted).ToList() };
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(cache, Json));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception)
            {
                // Кэш не обязателен: без него контакты просто подтянутся с сервера.
            }
        }

        private void ResetLocal(bool forgetKey)
        {
            _items.Clear();
            _contacts = new List<Contact>();
            _seq = 0;
            _vaultId = null;
            if (forgetKey)
            {
                _key = null;
                _settings.ProtectedKey = string.Empty;
            }

            var path = CachePath(_settings);
            if (path != null && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private class CloudCache
        {
            public long Seq { get; set; }

            public string VaultId { get; set; }

            public List<CloudItem> Items { get; set; }
        }

        /// <summary>Содержимое записи до шифрования.</summary>
        private class CloudContactPayload
        {
            public string Name { get; set; }

            public string Comment { get; set; }

            public string AnyDesk { get; set; }

            public string Rudesktop { get; set; }

            public string Assistant { get; set; }

            public string Ammyy { get; set; }

            public string Rdp { get; set; }

            public string Password { get; set; }

            public string RdpLogin { get; set; }

            public string RdpPassword { get; set; }

            public string Group { get; set; }

            public List<string> Labels { get; set; }
        }
    }
}
