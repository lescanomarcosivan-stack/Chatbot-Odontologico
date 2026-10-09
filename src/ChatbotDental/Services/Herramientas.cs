using System.Text.Json.Nodes;
using ChatbotDental.Data;

namespace ChatbotDental.Services;

/// <summary>
/// Herramientas ("tools") que el modelo puede usar. El modelo decide cuándo llamarlas;
/// el backend las ejecuta y le devuelve el resultado.
/// </summary>
public class Herramientas(IRepositorio repo, ILogger<Herramientas> log)
{
    /// <summary>Acción visible en el chat cuando una herramienta se ejecuta bien.</summary>
    public record Accion(string Tipo, string Texto);

    public static JsonArray Definiciones() => new()
    {
        Definir(
            "solicitar_turno",
            "Registra una solicitud de turno para que recepción la confirme por WhatsApp. " +
            "Usala solo cuando el paciente ya dio su nombre, su teléfono, el motivo o tratamiento, " +
            "y un día y franja horaria preferidos dentro del horario de atención. " +
            "Antes de llamarla, repetí los datos y pedí confirmación.",
            new()
            {
                ["nombre"] = Texto("Nombre y apellido del paciente."),
                ["telefono"] = Texto("Teléfono o WhatsApp de contacto, tal como lo escribió el paciente."),
                ["motivo"] = Texto("Tratamiento o motivo de la consulta, por ejemplo 'limpieza' o 'dolor de muela'."),
                ["fecha_preferida"] = Texto("Día preferido en formato AAAA-MM-DD."),
                ["franja"] = Enumerado("Franja horaria preferida.", "mañana", "tarde"),
                ["profesional"] = Texto("Profesional preferido, si el paciente lo indicó. Opcional."),
                ["es_urgencia"] = new JsonObject { ["type"] = "boolean", ["description"] = "true si hay dolor fuerte, inflamación o un golpe." },
            },
            "nombre", "telefono", "motivo", "fecha_preferida", "franja"),

        Definir(
            "registrar_consulta",
            "Guarda los datos de alguien que quiere que el consultorio lo contacte " +
            "(por ejemplo, para un presupuesto de ortodoncia o implantes) pero todavía no quiere un turno.",
            new()
            {
                ["nombre"] = Texto("Nombre del interesado."),
                ["telefono"] = Texto("Teléfono o WhatsApp de contacto."),
                ["consulta"] = Texto("Qué quiere saber o sobre qué tratamiento consulta."),
            },
            "nombre", "telefono", "consulta"),

        Definir(
            "derivar_a_recepcion",
            "Deja aviso a recepción para que una persona del consultorio continúe la conversación. " +
            "Usala cuando el paciente lo pide, cuando hay un reclamo, o cuando la pregunta no se puede " +
            "responder con la información del consultorio.",
            new()
            {
                ["nombre"] = Texto("Nombre de la persona."),
                ["telefono"] = Texto("Teléfono o WhatsApp de contacto."),
                ["motivo"] = Texto("Resumen breve de por qué se deriva."),
            },
            "nombre", "telefono", "motivo"),
    };

    /// <summary>Ejecuta una herramienta y devuelve el texto de resultado para el modelo.</summary>
    public async Task<(string Resultado, bool EsError, Accion? Accion)> EjecutarAsync(
        string nombre, JsonObject entrada, string conversacionId)
    {
        string Campo(string clave) => entrada[clave]?.GetValue<string>()?.Trim() ?? "";

        var nombrePaciente = Campo("nombre");
        var telefono = Campo("telefono");

        if (nombrePaciente.Length < 2)
            return ("Falta el nombre de la persona. Pedíselo antes de registrar.", true, null);
        if (telefono.Count(char.IsDigit) < 7)
            return ("El teléfono parece incompleto. Pedile que lo confirme con código de área.", true, null);

        try
        {
            switch (nombre)
            {
                case "solicitar_turno":
                {
                    var fecha = Campo("fecha_preferida");
                    if (!DateOnly.TryParse(fecha, out var dia))
                        return ("La fecha no es válida. Usá el formato AAAA-MM-DD.", true, null);
                    if (dia.DayOfWeek == DayOfWeek.Sunday)
                        return ("El consultorio no atiende los domingos. Ofrecé otro día.", true, null);
                    if (dia < Fechas.HoyArgentina())
                        return ("Esa fecha ya pasó. Pedí otra.", true, null);

                    var franja = Campo("franja");
                    if (dia.DayOfWeek == DayOfWeek.Saturday && franja == "tarde")
                        return ("Los sábados solo se atiende de 9 a 13. Ofrecé la mañana u otro día.", true, null);

                    var esUrgencia = entrada["es_urgencia"]?.GetValue<bool>() ?? false;
                    var profesional = Campo("profesional");
                    var detalle = Campo("motivo")
                                  + (profesional.Length > 0 ? $", prefiere {profesional}" : "")
                                  + (esUrgencia ? " [URGENCIA]" : "");

                    var id = await repo.GuardarSolicitudAsync(new NuevaSolicitud(
                        conversacionId, TipoSolicitud.Turno, nombrePaciente, telefono, detalle,
                        dia.ToString("yyyy-MM-dd"), franja));

                    return ($"Solicitud de turno #{id} registrada. Recepción la confirma por WhatsApp.",
                        false, new Accion("turno", $"Turno solicitado para el {Fechas.Legible(dia)} por la {franja}"));
                }

                case "registrar_consulta":
                {
                    var id = await repo.GuardarSolicitudAsync(new NuevaSolicitud(
                        conversacionId, TipoSolicitud.Consulta, nombrePaciente, telefono, Campo("consulta"), null, null));
                    return ($"Consulta #{id} registrada. El consultorio se va a comunicar.",
                        false, new Accion("consulta", "Tus datos quedaron registrados para que te contacten"));
                }

                case "derivar_a_recepcion":
                {
                    var id = await repo.GuardarSolicitudAsync(new NuevaSolicitud(
                        conversacionId, TipoSolicitud.Humano, nombrePaciente, telefono, Campo("motivo"), null, null));
                    return ($"Aviso #{id} enviado a recepción.",
                        false, new Accion("humano", "Recepción recibió tu mensaje y te va a escribir"));
                }

                default:
                    return ($"La herramienta '{nombre}' no existe.", true, null);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error al ejecutar la herramienta {Herramienta}", nombre);
            return ("No se pudo guardar por un error interno. Pedí disculpas y ofrecé el WhatsApp de recepción.", true, null);
        }
    }

    private static JsonObject Definir(string nombre, string descripcion, JsonObject propiedades, params string[] requeridos) => new()
    {
        ["name"] = nombre,
        ["description"] = descripcion,
        ["input_schema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = propiedades,
            ["required"] = new JsonArray(requeridos.Select(r => (JsonNode)r).ToArray()),
        },
    };

    private static JsonObject Texto(string descripcion) =>
        new() { ["type"] = "string", ["description"] = descripcion };

    private static JsonObject Enumerado(string descripcion, params string[] valores) => new()
    {
        ["type"] = "string",
        ["description"] = descripcion,
        ["enum"] = new JsonArray(valores.Select(v => (JsonNode)v).ToArray()),
    };
}
