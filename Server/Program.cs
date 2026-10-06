using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using RemoteAccessAddressBook.Server;

// Сервер облачной базы Remote Access — Address Book.
// Хранит только шифротексты: контакты шифруются и расшифровываются в приложении
// ключом из парольной фразы команды, сервер её не знает.

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("RAAB_")
    .Build();

var dataDirectory = configuration["DataDirectory"] ?? "data";
if (!Path.IsPathRooted(dataDirectory))
{
    dataDirectory = Path.Combine(AppContext.BaseDirectory, dataDirectory);
}

var database = new ServerDatabase(Path.Combine(dataDirectory, "server.db"));
database.EnsureCreated();

if (AdminCli.IsCommand(args))
{
    return AdminCli.Run(args, database);
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,

    // У службы Windows текущий каталог — System32, поэтому явно указываем каталог программы.
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddEnvironmentVariables("RAAB_");

// Одинаково работает как служба Windows, служба systemd и обычный консольный процесс.
builder.Host.UseWindowsService(options => options.ServiceName = "RemoteAccessAddressBook");
builder.Host.UseSystemd();

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);

builder.Services.AddSingleton(database);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Подбор паролей: не больше 10 попыток входа в минуту с одного адреса.
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Сервер стоит за Caddy/nginx на этом же компьютере: настоящий адрес клиента — в X-Forwarded-For.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
app.UseRateLimiter();

var tokenLifetime = TimeSpan.FromDays(configuration.GetValue("TokenLifetimeDays", 90));
var serverVersion = typeof(ServerDatabase).Assembly.GetName().Version?.ToString(3) ?? "?";

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", version = serverVersion }));

app.MapPost("/api/auth/login", (LoginRequest request, ServerDatabase db) =>
{
    var user = db.FindUser(request?.Login?.Trim());
    if (user == null || user.Disabled || !PasswordHasher.Verify(request.Password, user.PasswordHash))
    {
        return Results.Json(new ErrorResponse("Неверный логин или пароль."), statusCode: StatusCodes.Status401Unauthorized);
    }

    var token = PasswordHasher.NewToken();
    var expiresAt = DateTime.UtcNow + tokenLifetime;
    db.AddToken(PasswordHasher.HashToken(token), user.Id, expiresAt);
    return Results.Ok(new LoginResponse(token, expiresAt, user.Login, user.IsAdmin));
}).RequireRateLimiting("login");

var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
{
    var http = context.HttpContext;
    var header = http.Request.Headers.Authorization.ToString();
    var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring(7).Trim() : null;
    var db = http.RequestServices.GetRequiredService<ServerDatabase>();
    var user = string.IsNullOrEmpty(token) ? null : db.FindUserByToken(PasswordHasher.HashToken(token), tokenLifetime);
    if (user == null)
    {
        return Results.Json(new ErrorResponse("Требуется вход."), statusCode: StatusCodes.Status401Unauthorized);
    }

    http.Items["user"] = user;
    http.Items["tokenHash"] = PasswordHasher.HashToken(token);
    return await next(context);
});

api.MapPost("/auth/logout", (HttpContext http, ServerDatabase db) =>
{
    db.DeleteToken((string)http.Items["tokenHash"]);
    return Results.Ok();
});

api.MapGet("/me", (HttpContext http) =>
{
    var user = (UserRecord)http.Items["user"];
    return Results.Ok(new { login = user.Login, isAdmin = user.IsAdmin });
});

api.MapGet("/vault", (ServerDatabase db) =>
{
    var vault = db.GetVault();
    return Results.Ok(vault == null
        ? new VaultDto(false, null, 0, null)
        : new VaultDto(true, Convert.ToBase64String(vault.Salt), vault.Iterations, Convert.ToBase64String(vault.Check)));
});

// Первая настройка хранилища: соль и проверочный блоб парольной фразы задаёт администратор.
api.MapPost("/vault", (VaultDto request, HttpContext http, ServerDatabase db) =>
{
    var user = (UserRecord)http.Items["user"];
    if (!user.IsAdmin)
    {
        return Results.Json(new ErrorResponse("Задать парольную фразу может только администратор."), statusCode: StatusCodes.Status403Forbidden);
    }

    if (string.IsNullOrEmpty(request?.Salt) || string.IsNullOrEmpty(request.Check) || request.Iterations < 100_000)
    {
        return Results.BadRequest(new ErrorResponse("Неверные параметры хранилища."));
    }

    var created = db.InitVault(new VaultRecord(
        Convert.FromBase64String(request.Salt), request.Iterations, Convert.FromBase64String(request.Check)));
    return created
        ? Results.Ok()
        : Results.Conflict(new ErrorResponse("Парольная фраза уже задана."));
});

api.MapGet("/items", (long? since, ServerDatabase db) =>
{
    var (seq, items) = db.GetItemsSince(since ?? 0);
    return Results.Ok(new ItemsResponse(seq, VaultId(db.GetVault()), items.Select(ItemDto.From).ToList()));
});

api.MapPut("/items/{id}", (string id, PutItemRequest request, HttpContext http, ServerDatabase db) =>
{
    if (!Guid.TryParse(id, out _))
    {
        return Results.BadRequest(new ErrorResponse("Неверный идентификатор записи."));
    }

    if (string.IsNullOrEmpty(request?.Data))
    {
        return Results.BadRequest(new ErrorResponse("Пустые данные."));
    }

    if (db.GetVault() == null)
    {
        return Results.Json(new ErrorResponse("Хранилище не настроено: администратор ещё не задал парольную фразу."), statusCode: StatusCodes.Status409Conflict);
    }

    var user = (UserRecord)http.Items["user"];
    var (outcome, item) = db.PutItem(id.ToLowerInvariant(), Convert.FromBase64String(request.Data), request.BaseVersion, user.Login);
    return outcome == WriteOutcome.Ok
        ? Results.Ok(ItemDto.From(item))
        : Results.Json(new ConflictResponse("Запись изменили на другом компьютере.", item == null ? null : ItemDto.From(item)), statusCode: StatusCodes.Status409Conflict);
});

api.MapDelete("/items/{id}", (string id, long baseVersion, HttpContext http, ServerDatabase db) =>
{
    if (!Guid.TryParse(id, out _))
    {
        return Results.BadRequest(new ErrorResponse("Неверный идентификатор записи."));
    }

    var user = (UserRecord)http.Items["user"];
    var (outcome, item) = db.DeleteItem(id.ToLowerInvariant(), baseVersion, user.Login);
    return outcome == WriteOutcome.Ok
        ? Results.Ok()
        : Results.Json(new ConflictResponse("Запись изменили на другом компьютере.", item == null ? null : ItemDto.From(item)), statusCode: StatusCodes.Status409Conflict);
});

app.Run();
return 0;

// Идентификатор хранилища: меняется при сбросе — клиент сбрасывает свой кэш.
static string VaultId(VaultRecord vault) =>
    vault == null ? null : Convert.ToHexString(SHA256.HashData(vault.Check)).Substring(0, 16);

public record LoginRequest(string Login, string Password);

public record LoginResponse(string Token, DateTime ExpiresAt, string Login, bool IsAdmin);

public record ErrorResponse(string Error);

public record VaultDto(bool Initialized, string Salt, int Iterations, string Check);

public record PutItemRequest(string Data, long BaseVersion);

public record ItemDto(string Id, long Version, bool Deleted, string Data, long Seq, string UpdatedAt, string UpdatedBy)
{
    public static ItemDto From(ItemRecord item) => new ItemDto(
        item.Id, item.Version, item.Deleted, item.Data == null ? null : Convert.ToBase64String(item.Data), item.Seq, item.UpdatedAt, item.UpdatedBy);
}

public record ItemsResponse(long Seq, string VaultId, List<ItemDto> Items);

public record ConflictResponse(string Error, ItemDto Current);
