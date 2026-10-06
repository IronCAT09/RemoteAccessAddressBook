# Сервер облачной базы

Небольшой веб-сервер для общей облачной базы контактов Remote Access — Address Book.
Работает на VPS под Ubuntu или Windows.

**Сервер не может прочитать контакты.** Приложение шифрует каждый контакт (AES-256-GCM)
ключом из парольной фразы команды ещё до отправки. Сервер хранит только шифротекст,
пользователей и хэши их паролей и токенов. Даже при взломе VPS или краже файла базы
контакты остаются зашифрованными.

## Как это устроено

```
Приложение ──HTTPS──▶ Caddy (443, сертификат Let's Encrypt) ──HTTP──▶ raab-server (127.0.0.1:5080) ──▶ data/server.db
```

- Сервер слушает только локальный порт 5080. Наружу смотрит Caddy: он сам получает и
  продлевает сертификат.
- Вход по логину и паролю, приложение получает токен (срок 90 дней, продлевается при
  работе). Не больше 10 попыток входа в минуту с одного адреса.
- Парольную фразу задаёт первый вошедший **администратор**. Остальные вводят её один
  раз на своём компьютере, и ключ сохраняется там в защищённом виде (DPAPI Windows).
- Каждая запись хранит номер версии. Если двое правят один контакт, второй получит
  вопрос о конфликте, а не перезапишет чужие правки молча.

## Сборка

Нужен .NET SDK 8.0. Из каталога `Server`:

```
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish -c Release -r win-x64   --self-contained true -p:PublishSingleFile=true
```

Результат: `bin/Release/net8.0/<платформа>/publish/` — программа `raab-server`
(`raab-server.exe`), библиотека SQLite `libe_sqlite3.so` (`e_sqlite3.dll`) и `appsettings.json`.
Эти три файла и нужны на сервере, .NET ставить не нужно. Остальное (`web.config`,
`aspnetcorev2_inprocess.dll`) нужно только для IIS, без него можно обойтись.

## Ubuntu

Нужен домен (или поддомен), A-запись которого указывает на IP сервера, например
`addr.example.com`.

```bash
# 1. Пользователь и каталоги
sudo useradd --system --home /opt/raab --shell /usr/sbin/nologin raab
sudo mkdir -p /opt/raab /var/lib/raab
sudo cp raab-server libe_sqlite3.so appsettings.json /opt/raab/
sudo chmod +x /opt/raab/raab-server
sudo chown -R raab:raab /var/lib/raab
sudo chmod 700 /var/lib/raab

# 2. Служба
sudo cp deploy/raab-server.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now raab-server
curl http://127.0.0.1:5080/api/health        # {"status":"ok",...}

# 3. Администратор и пользователи (пароль спросит)
sudo -u raab RAAB_DataDirectory=/var/lib/raab /opt/raab/raab-server user add admin --admin
sudo -u raab RAAB_DataDirectory=/var/lib/raab /opt/raab/raab-server user add ivan

# 4. Caddy (HTTPS)
sudo apt install -y caddy
sudo cp deploy/Caddyfile /etc/caddy/Caddyfile   # впишите свой домен
sudo systemctl reload caddy

# 5. Брандмауэр: наружу только 80/443 (и SSH)
sudo ufw allow OpenSSH && sudo ufw allow 80,443/tcp && sudo ufw enable
```

Журнал: `journalctl -u raab-server -f`.

Перед шагом 5 проверьте `sudo ss -tlnp`: если на VPS уже работают другие службы (VPN,
панели и т. п.), откройте в `ufw` и их порты, иначе `ufw enable` их отрежет.

**Если порт 443 занят** другой программой, Caddy можно повесить на любой свободный порт.
Сертификат он всё равно получит через порт 80, нужно только, чтобы порт 80 был свободен:

```
addr.example.com:8443 {
	reverse_proxy 127.0.0.1:5080
}
```

В приложении тогда адрес сервера указывается с портом: `addr.example.com:8443`.

## Windows Server

1. Скопируйте `raab-server.exe`, `e_sqlite3.dll`, `appsettings.json` и `deploy\install-windows-service.ps1` в один
   каталог. Запустите PowerShell от имени администратора:
   ```powershell
   .\install-windows-service.ps1 -InstallDir C:\raab
   & C:\raab\raab-server.exe user add admin --admin
   ```
2. HTTPS через Caddy: скачайте `caddy.exe` с https://caddyserver.com/download, положите
   рядом `deploy\Caddyfile` со своим доменом и зарегистрируйте службу:
   ```powershell
   sc.exe create caddy start= auto binPath= "C:\caddy\caddy.exe run --config C:\caddy\Caddyfile"
   sc.exe start caddy
   ```
   Вместо Caddy подойдёт IIS с модулем ARR (reverse proxy на `http://127.0.0.1:5080`).
3. В брандмауэре Windows откройте входящие 80 и 443 TCP. Порт 5080 наружу не открывайте.

## Управление пользователями

| Команда | Что делает |
| --- | --- |
| `raab-server user add <логин> [--admin]` | создать пользователя (пароль не короче 8 символов) |
| `raab-server user passwd <логин>` | сменить пароль и завершить все сеансы |
| `raab-server user disable <логин>` | заблокировать и завершить сеансы (например, при увольнении) |
| `raab-server user enable <логин>` | разблокировать |
| `raab-server user admin\|noadmin <логин>` | дать или снять права администратора |
| `raab-server user delete <логин>` | удалить |
| `raab-server user list` | список |
| `raab-server vault reset --yes` | **удалить все контакты** и парольную фразу (если фраза утеряна) |

Пароль можно передать через стандартный ввод: `echo 'пароль' | raab-server user add ivan`.

После блокировки сотрудника он больше не получит изменений с сервера. Но контакты,
которые он уже видел, остаются у него в кэше, а фразу он знает. Если это важно, смените
фразу: выполните `vault reset`, затем администратор заново задаёт фразу и переносит
контакты из своей локальной базы в облако синхронизацией.

## Настройки

`appsettings.json` рядом с программой или переменные окружения с префиксом `RAAB_`:

| Параметр | По умолчанию | Назначение |
| --- | --- | --- |
| `Urls` | `http://127.0.0.1:5080` | адрес, который слушает сервер |
| `DataDirectory` | `data` (рядом с программой) | каталог с `server.db` |
| `TokenLifetimeDays` | `90` | срок жизни токена без использования |

## Резервная копия

Всё хранится в `server.db` (режим WAL, рядом бывают `-wal` и `-shm`). Безопасная копия
на работающем сервере:

```bash
sqlite3 /var/lib/raab/server.db ".backup '/root/raab-$(date +%F).db'"
```

Копия зашифрована так же, как сама база: без парольной фразы контакты из неё не прочитать.

## API

| Метод | Путь | Описание |
| --- | --- | --- |
| GET | `/api/health` | проверка работы (без входа) |
| POST | `/api/auth/login` | `{login, password}` → `{token, expiresAt, login, isAdmin}` |
| POST | `/api/auth/logout` | отозвать текущий токен |
| GET | `/api/vault` | соль, число итераций и проверочный блоб фразы |
| POST | `/api/vault` | первая настройка фразы (только администратор) |
| GET | `/api/items?since=N` | записи, изменённые после номера N |
| PUT | `/api/items/{id}` | `{data, baseVersion}` → новая версия или 409 с текущей |
| DELETE | `/api/items/{id}?baseVersion=N` | удалить (или 409) |

Все запросы, кроме `health` и `login`, требуют заголовка `Authorization: Bearer <токен>`.
