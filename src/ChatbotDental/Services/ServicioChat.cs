using System.Text;
using System.Text.Json.Nodes;
using ChatbotDental.Data;

namespace ChatbotDental.Services;

public record RespuestaChat(string Respuesta, IReadOnlyList<Herramientas.Accion> Acciones);

/// <summary>
/// Orquesta un turno de conversación: arma el contexto, llama a Claude,
/// ejecuta las herramientas que pida y devuelve el texto final.
/// </summary>
public class ServicioChat(
    ClienteClaude claude,
    Herramientas herramientas,
    IRepositorio repo,
    BaseConocimiento conocimiento,
    ILogger<ServicioChat> log)
{
    private const int MensajesDeContexto = 20;   // historial que se le manda al modelo
    private const int MaxVueltasHerramientas = 4; // evita bucles infinitos de herramientas

    private static readonly JsonArray DefinicionesHerramientas = Herramientas.Definiciones();

    public async Task<RespuestaChat> ResponderAsync(string conversacionId, string textoUsuario, string? ip, CancellationToken ct)
    {
        // 1. Historial previo + mensaje nuevo
        var historial = await repo.ObtenerMensajesAsync(conversacionId, MensajesDeContexto);
        await repo.AgregarMensajeAsync(conversacionId, "user", textoUsuario, ip);

        var mensajes = new JsonArray();
        foreach (var m in historial)
            mensajes.Add(new JsonObject { ["role"] = m.Rol, ["content"] = m.Texto });
        mensajes.Add(new JsonObject { ["role"] = "user", ["content"] = textoUsuario });

        // La API exige que el primer mensaje sea del usuario.
        while (mensajes.Count > 0 && mensajes[0]?["role"]?.GetValue<string>() != "user")
            mensajes.RemoveAt(0);

        var sistema = ArmarSistema();
        var acciones = new List<Herramientas.Accion>();
        var textoFinal = new StringBuilder();

        // 2. Bucle de herramientas: mientras el modelo pida herramientas, se ejecutan y se le devuelven.
        for (var vuelta = 0; vuelta <= MaxVueltasHerramientas; vuelta++)
        {
            var respuesta = await claude.EnviarAsync(sistema, mensajes, DefinicionesHerramientas, ct);
            var bloques = respuesta["content"]?.AsArray() ?? new JsonArray();
            var motivo = respuesta["stop_reason"]?.GetValue<string>();

            textoFinal.Clear();
            foreach (var b in bloques)
                if (b?["type"]?.GetValue<string>() == "text")
                    textoFinal.Append(b["text"]?.GetValue<string>());

            if (motivo != "tool_use" || vuelta == MaxVueltasHerramientas)
                break;

            mensajes.Add(new JsonObject { ["role"] = "assistant", ["content"] = bloques.DeepClone() });

            var resultados = new JsonArray();
            foreach (var b in bloques)
            {
                if (b?["type"]?.GetValue<string>() != "tool_use") continue;

                var nombre = b["name"]!.GetValue<string>();
                var entrada = b["input"]?.AsObject() ?? new JsonObject();
                var (resultado, esError, accion) = await herramientas.EjecutarAsync(nombre, entrada, conversacionId);
                log.LogInformation("Herramienta {Nombre} → {Resultado}", nombre, resultado);
                if (accion is not null) acciones.Add(accion);

                resultados.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = b["id"]!.GetValue<string>(),
                    ["content"] = resultado,
                    ["is_error"] = esError,
                });
            }
            mensajes.Add(new JsonObject { ["role"] = "user", ["content"] = resultados });
        }

        var texto = textoFinal.ToString().Trim();
        if (texto.Length == 0)
            texto = acciones.Count > 0
                ? "Listo, ya quedó registrado. ¿Te ayudo con algo más?"
                : "Perdón, no pude armar una respuesta. ¿Me lo escribís de otra forma?";

        await repo.AgregarMensajeAsync(conversacionId, "assistant", texto, null);
        return new RespuestaChat(texto, acciones);
    }

    private string ArmarSistema() => $"""
        Sos Lapi, el asistente virtual del Estudio Dental Lapacho, un consultorio odontológico en Yerba Buena, Tucumán.
        Hablás en español rioplatense, con voseo, de forma cálida, clara y profesional.

        Fecha y hora actual en Argentina: {Fechas.AhoraLegible()}.
        Usala para interpretar expresiones como "mañana", "el jueves" o "la semana que viene".

        Tus tareas:
        - Responder consultas sobre el consultorio usando SOLO la información de abajo.
        - Ayudar a pedir un turno: juntá nombre, teléfono, motivo, día y franja (mañana o tarde),
          respetando los días de cada profesional y el horario. Repetí los datos y pedí confirmación
          antes de usar la herramienta solicitar_turno.
        - Si alguien quiere un presupuesto o que lo llamen, usá registrar_consulta.
        - Si piden hablar con una persona, hay un reclamo, o no sabés la respuesta, ofrecé derivar_a_recepcion.

        Reglas:
        - Nunca inventes precios, profesionales, horarios ni obras sociales que no estén en la información.
          Si no está, decí que no tenés ese dato y ofrecé derivar a recepción.
        - No des diagnósticos ni indiques medicamentos. Ante dolor fuerte, inflamación o un golpe,
          ofrecé un turno de urgencia. Si hay sangrado que no para, fiebre alta o hinchazón que dificulta
          respirar o tragar, indicá llamar al 107 o ir a una guardia.
        - Los turnos que registrás son solicitudes: recepción los confirma por WhatsApp. Dejalo claro.
        - Pedí los datos de a poco (uno o dos por mensaje), no todo junto.
        - Respuestas breves: 1 a 4 oraciones. Podés usar listas cortas si ayudan. Sin emojis en exceso.
        - Si te preguntan algo que no tiene que ver con el consultorio, respondé amablemente que solo
          podés ayudar con temas del consultorio.

        Información del consultorio:
        <consultorio>
        {conocimiento.Texto}
        </consultorio>
        """;
}

/// <summary>Carga el archivo con la información del consultorio (editable sin tocar código).</summary>
public class BaseConocimiento
{
    public string Texto { get; }

    public BaseConocimiento(IWebHostEnvironment env)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Conocimiento", "consultorio.md");
        if (!File.Exists(ruta))
            ruta = Path.Combine(env.ContentRootPath, "Conocimiento", "consultorio.md");
        Texto = File.ReadAllText(ruta);
    }
}
