using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>Сервер ответил «нужен вход»: токен истёк, отозван или пользователь заблокирован.</summary>
    public class CloudAuthException : Exception
    {
        public CloudAuthException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Ошибка обращения к серверу с понятным текстом.</summary>
    public class CloudException : Exception
    {
        public CloudException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }

    public class CloudLoginResult
    {
        public string Token { get; set; }

        public DateTime ExpiresAt { get; set; }

        public string Login { get; set; }

        public bool IsAdmin { get; set; }
    }

    public class CloudVault
    {
        public bool Initialized { get; set; }

        public string Salt { get; set; }

        public int Iterations { get; set; }

        public string Check { get; set; }
    }

    public class CloudItem
    {
        public string Id { get; set; }

        public long Version { get; set; }

        public bool Deleted { get; set; }

        /// <summary>Шифротекст в base64.</summary>
        public string Data { get; set; }

        public long Seq { get; set; }

        public string UpdatedAt { get; set; }

        public string UpdatedBy { get; set; }
    }

    public class CloudItemsPage
    {
        public long Seq { get; set; }

        public string VaultId { get; set; }

        public List<CloudItem> Items { get; set; } = new List<CloudItem>();
    }

    /// <summary>Результат записи: Item — новая версия, или Current — чужая версия при конфликте.</summary>
    public class CloudWriteResult
    {
        public bool Conflict { get; set; }

        public CloudItem Item { get; set; }

        public CloudItem Current { get; set; }
    }

    /// <summary>HTTP-клиент сервера облачной базы.</summary>
    public class CloudClient : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;

        public CloudClient(string serverUrl, string token = null)
        {
            BaseUrl = NormalizeUrl(serverUrl);
            _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(20) };
            if (!string.IsNullOrEmpty(token))
            {
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        public string BaseUrl { get; }

        /// <summary>«addr.example.com» → «https://addr.example.com», без завершающего «/».</summary>
        public static string NormalizeUrl(string url)
        {
            url = (url ?? string.Empty).Trim().TrimEnd('/');
            if (url.Length == 0)
            {
                throw new CloudException("Не указан адрес сервера.");
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                throw new CloudException("Неверный адрес сервера: " + url);
            }

            return url;
        }

        public async Task<CloudLoginResult> LoginAsync(string login, string password)
        {
            var response = await Send(() => _http.PostAsJsonAsync("api/auth/login", new { login, password }, Json));
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new CloudAuthException(await ErrorText(response, "Неверный логин или пароль."));
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                throw new CloudException("Слишком много попыток входа. Подождите минуту.");
            }

            await EnsureSuccess(response);
            return await response.Content.ReadFromJsonAsync<CloudLoginResult>(Json);
        }

        public async Task LogoutAsync()
        {
            try
            {
                using var response = await _http.PostAsync("api/auth/logout", null);
            }
            catch (Exception)
            {
                // Выход локальный в любом случае: токен просто забываем.
            }
        }

        public async Task<CloudVault> GetVaultAsync()
        {
            var response = await Send(() => _http.GetAsync("api/vault"));
            await EnsureSuccess(response);
            return await response.Content.ReadFromJsonAsync<CloudVault>(Json);
        }

        public async Task InitVaultAsync(byte[] salt, int iterations, byte[] check)
        {
            var response = await Send(() => _http.PostAsJsonAsync("api/vault", new CloudVault
            {
                Initialized = true,
                Salt = Convert.ToBase64String(salt),
                Iterations = iterations,
                Check = Convert.ToBase64String(check),
            }, Json));
            await EnsureSuccess(response);
        }

        public async Task<CloudItemsPage> GetItemsAsync(long since)
        {
            var response = await Send(() => _http.GetAsync("api/items?since=" + since));
            await EnsureSuccess(response);
            return await response.Content.ReadFromJsonAsync<CloudItemsPage>(Json);
        }

        public async Task<CloudWriteResult> PutItemAsync(string id, byte[] data, long baseVersion)
        {
            var response = await Send(() => _http.PutAsJsonAsync("api/items/" + id, new { data = Convert.ToBase64String(data), baseVersion }, Json));
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var conflict = await ReadConflict(response);
                if (conflict.Current == null && conflict.Error != null && baseVersion == 0)
                {
                    throw new CloudException(conflict.Error);
                }

                return new CloudWriteResult { Conflict = true, Current = conflict.Current };
            }

            await EnsureSuccess(response);
            return new CloudWriteResult { Item = await response.Content.ReadFromJsonAsync<CloudItem>(Json) };
        }

        public async Task<CloudWriteResult> DeleteItemAsync(string id, long baseVersion)
        {
            var response = await Send(() => _http.DeleteAsync("api/items/" + id + "?baseVersion=" + baseVersion));
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new CloudWriteResult { Conflict = true, Current = (await ReadConflict(response)).Current };
            }

            await EnsureSuccess(response);
            return new CloudWriteResult();
        }

        public void Dispose() => _http.Dispose();

        private static async Task<HttpResponseMessage> Send(Func<Task<HttpResponseMessage>> send)
        {
            try
            {
                return await send();
            }
            catch (TaskCanceledException ex)
            {
                throw new CloudException("Сервер не ответил вовремя.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new CloudException("Нет связи с сервером: " + ex.Message, ex);
            }
        }

        private static async Task EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new CloudAuthException(await ErrorText(response, "Требуется вход на сервер."));
            }

            if ((int)response.StatusCode >= 500)
            {
                // 502/503/504 — обычно прокси (Caddy) работает, а сам сервер облачной базы остановлен.
                throw new CloudException("Сервер облачной базы недоступен (ошибка " + (int)response.StatusCode + ").");
            }

            throw new CloudException(await ErrorText(response, "Сервер вернул ошибку " + (int)response.StatusCode + "."));
        }

        private static async Task<string> ErrorText(HttpResponseMessage response, string fallback)
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ConflictBody>(Json);
                return string.IsNullOrWhiteSpace(error?.Error) ? fallback : error.Error;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static async Task<ConflictBody> ReadConflict(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadFromJsonAsync<ConflictBody>(Json) ?? new ConflictBody();
            }
            catch (Exception)
            {
                return new ConflictBody();
            }
        }

        private class ConflictBody
        {
            public string Error { get; set; }

            public CloudItem Current { get; set; }
        }
    }
}
