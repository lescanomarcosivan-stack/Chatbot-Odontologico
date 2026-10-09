namespace ChatbotDental.Data;

/// <summary>Un mensaje de texto de una conversación (usuario o asistente).</summary>
public record Mensaje(string Rol, string Texto, DateTimeOffset Fecha);

/// <summary>Resumen de una conversación para el panel de administración.</summary>
public record ResumenConversacion(
    string Id,
    DateTimeOffset Inicio,
    DateTimeOffset UltimoMensaje,
    int CantidadMensajes,
    string PrimerMensaje);

/// <summary>Tipos de solicitud que el bot puede registrar.</summary>
public static class TipoSolicitud
{
    public const string Turno = "turno";
    public const string Consulta = "consulta";
    public const string Humano = "humano";
}

/// <summary>
/// Una solicitud registrada por el bot: pedido de turno, contacto para consulta
/// o derivación a una persona del consultorio.
/// </summary>
public record Solicitud(
    long Id,
    string ConversacionId,
    string Tipo,
    string Nombre,
    string Telefono,
    string Detalle,
    string? FechaPreferida,
    string? Franja,
    DateTimeOffset Fecha);

public record NuevaSolicitud(
    string ConversacionId,
    string Tipo,
    string Nombre,
    string Telefono,
    string Detalle,
    string? FechaPreferida,
    string? Franja);
