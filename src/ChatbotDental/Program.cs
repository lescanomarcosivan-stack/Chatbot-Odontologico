using System.Security.Cryptography;
using System.Text;
using ChatbotDental.Data;
using ChatbotDental.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuración (variables de entorno) ----------
string? Env(string nombre) => Environment.GetEnvironmentVariable(nombre) is { Length: > 0 } v ? v : null;

var opcionesClaude = new OpcionesClaude
{
    ApiKey = Env("ANTHROPIC_API_KEY") ?? "",
    Modelo = Env("ANTHROPIC_MODEL") ?? "claude-haiku-5-5",
    UrlBase = Env("ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com",
};
var opcionesLimite = new OpcionesLimite
{
    MensajesPorConversacion = int.TryParse(Env("LIMITE_POR_CONVERSACION"), out var a) ? a : 25,
    MensajesPorIpPorDia = int.TryParse(Env("LIMITE_POR_IP_DIA"), out var b) ? b : 60,
    MensajesTotalesPorDia = int.TryParse(Env("LIMITE_TOTAL_DIA"), out var c) ? c : 1500,
};
var claveAdmin = Env("ADMIN_PASSWORD");
var urlBase = Env("DATABASE_URL");

builder.Services.AddSingleton(opcionesClaude);
builder.Services.AddSingleton(new LimiteUso(opcionesLimite));
builder.Services.AddSingleton<BaseConocimiento>();
builder.Services.AddHttpClient<ClienteClaude>();
builder.Services.AddScoped<Herramientas>();
builder.Services.AddScoped<ServicioChat>();

if (urlBase is not null)
    builder.Services.AddSingleton<IRepositorio>(new RepositorioPostgres(RepositorioPostgres.DesdeUrl(urlBase)));
else
    builder.Services.AddSingleton<IRepositorio, RepositorioMemoria>();

// Railway (y la mayoría de los hostings) pasan por un proxy: confiamos en X-Forwarded-For para la IP real.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// Railway indica el puerto en la variable PORT.
if (Env("PORT") is { } puerto)
    builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}");

var app = builder.Build();

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();

var repo = app.Services.GetRequiredService<IRepositorio>();
await repo.InicializarAsync();
app.Logger.LogInformation("Base de datos: {Tipo}. Modelo: {Modelo}. API key: {Key}",
    urlBase is null ? "en memoria (los datos se pierden al reiniciar)" : "PostgreSQL",
    opcionesClaude.Modelo,
    opcionesClaude.ApiKey.Length > 0 ? "configurada" : "FALTA ANTHROPIC_API_KEY");

// ---------- API del chat ----------
app.MapPost("/api/chat", async (PedidoChat pedido, HttpContext http, ServicioChat chat,
    LimiteUso limite, ClienteClaude claude, IRepositorio repo, CancellationToken ct) =>
{
    var texto = pedido.Mensaje?.Trim() ?? "";
    var conversacionId = pedido.ConversacionId?.Trim() ?? "";

    if (conversacionId.Length is < 8 or > 64 || !conversacionId.All(ch => char.IsLetterOrDigit(ch) || ch == '-'))
        return Results.BadRequest(new { error = "Identificador de conversación inválido." });
    if (texto.Length == 0)
        return Results.BadRequest(new { error = "Escribí un mensaje." });
    if (texto.Length > limite.Opciones.LargoMaximo)
        return Results.BadRequest(new { error = $"El mensaje es muy largo. Máximo {limite.Opciones.LargoMaximo} caracteres." });
    if (!claude.Configurado)
        return Results.Json(new { error = "El asistente no está configurado: falta la clave de la API." }, statusCode: 503);

    if (await repo.ContarMensajesUsuarioAsync(conversacionId) >= limite.Opciones.MensajesPorConversacion)
        return Results.Json(new
        {
            error = "Esta conversación llegó al límite del demo. Empezá una nueva o escribinos por WhatsApp.",
        }, statusCode: 429);

    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
    if (limite.Verificar(ip) is { } motivo)
        return Results.Json(new { error = motivo }, statusCode: 429);

    try
    {
        var respuesta = await chat.ResponderAsync(conversacionId, texto, ip, ct);
        return Results.Ok(new { respuesta = respuesta.Respuesta, acciones = respuesta.Acciones });
    }
    catch (ErrorApiClaude ex) when (ex.Estado == 429 || ex.Estado >= 500)
    {
        return Results.Json(new { error = "El asistente está con mucha demanda. Probá de nuevo en unos segundos." }, statusCode: 503);
    }
    catch (Exception ex) when (ex is ErrorApiClaude or HttpRequestException or TaskCanceledException)
    {
        app.Logger.LogError(ex, "Fallo al responder");
        return Results.Json(new { error = "No pudimos responder ahora. Probá de nuevo o escribinos por WhatsApp." }, statusCode: 502);
    }
});

app.MapGet("/api/estado", (ClienteClaude claude) => Results.Ok(new { asistenteDisponible = claude.Configurado }));

// ---------- API del panel de administración ----------
var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
{
    var enviada = ctx.HttpContext.Request.Headers["X-Admin-Key"].ToString();
    if (claveAdmin is null)
        return Results.Json(new { error = "El panel está deshabilitado: configurá ADMIN_PASSWORD." }, statusCode: 503);
    var ok = CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(enviada)),
        SHA256.HashData(Encoding.UTF8.GetBytes(claveAdmin)));
    return ok ? await next(ctx) : Results.Json(new { error = "Contraseña incorrecta." }, statusCode: 401);
});

admin.MapGet("/solicitudes", async (IRepositorio repo) => Results.Ok(await repo.ListarSolicitudesAsync(200)));
admin.MapGet("/conversaciones", async (IRepositorio repo) => Results.Ok(await repo.ListarConversacionesAsync(100)));
admin.MapGet("/conversaciones/{id}", async (string id, IRepositorio repo) =>
    Results.Ok(await repo.ObtenerConversacionCompletaAsync(id)));

app.Run();

record PedidoChat(string? ConversacionId, string? Mensaje);
