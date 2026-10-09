namespace ChatbotDental.Data;

/// <summary>
/// Acceso a datos. Hay dos implementaciones: PostgreSQL (producción)
/// y en memoria (para probar en local sin instalar una base).
/// </summary>
public interface IRepositorio
{
    Task InicializarAsync();

    Task AgregarMensajeAsync(string conversacionId, string rol, string texto, string? ip);
    Task<IReadOnlyList<Mensaje>> ObtenerMensajesAsync(string conversacionId, int ultimos);
    Task<int> ContarMensajesUsuarioAsync(string conversacionId);

    Task<long> GuardarSolicitudAsync(NuevaSolicitud solicitud);
    Task<IReadOnlyList<Solicitud>> ListarSolicitudesAsync(int limite);

    Task<IReadOnlyList<ResumenConversacion>> ListarConversacionesAsync(int limite);
    Task<IReadOnlyList<Mensaje>> ObtenerConversacionCompletaAsync(string conversacionId);
}
