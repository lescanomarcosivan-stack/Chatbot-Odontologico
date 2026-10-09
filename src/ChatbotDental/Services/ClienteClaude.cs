using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChatbotDental.Services;

public class OpcionesClaude
{
    public string ApiKey { get; set; } = "";
    public string Modelo { get; set; } = "claude-haiku-5-5";
    public string UrlBase { get; set; } = "https://api.anthropic.com";
    public int MaxTokens { get; set; } = 700;
}

/// <summary>
/// Cliente mínimo para la Messages API de Anthropic usando HttpClient.
/// Se trabaja con JsonNode para poder reenviar tal cual los bloques que devuelve
/// el modelo (texto y llamadas a herramientas).
/// </summary>
public class ClienteClaude
{
    private readonly HttpClient _http;
    private readonly OpcionesClaude _opciones;
    private readonly ILogger<ClienteClaude> _log;

    public ClienteClaude(HttpClient http, OpcionesClaude opciones, ILogger<ClienteClaude> log)
    {
        _http = http;
        _opciones = opciones;
        _log = log;
        _http.BaseAddress = new Uri(opciones.UrlBase.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public bool Configurado => !string.IsNullOrWhiteSpace(_opciones.ApiKey);

    /// <summary>Envía la conversación y devuelve la respuesta completa del modelo.</summary>
    public async Task<JsonObject> EnviarAsync(string sistema, JsonArray mensajes, JsonArray herramientas, CancellationToken ct)
    {
        var cuerpo = new JsonObject
        {
            ["model"] = _opciones.Modelo,
            ["max_tokens"] = _opciones.MaxTokens,
            ["system"] = sistema,
            ["tools"] = herramientas.DeepClone(),
            ["messages"] = mensajes.DeepClone(),
        };

        using var pedido = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        pedido.Headers.Add("x-api-key", _opciones.ApiKey);
        pedido.Headers.Add("anthropic-version", "2023-06-01");

        using var respuesta = await _http.SendAsync(pedido, ct);
        var texto = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            _log.LogError("La API de Claude respondió {Estado}: {Cuerpo}", (int)respuesta.StatusCode, texto);
            throw new ErrorApiClaude((int)respuesta.StatusCode);
        }

        return JsonNode.Parse(texto)?.AsObject()
               ?? throw new JsonException("Respuesta vacía de la API de Claude");
    }
}

public class ErrorApiClaude(int estado) : Exception($"Error de la API de Claude ({estado})")
{
    public int Estado { get; } = estado;
}
