using System.Collections.Concurrent;

namespace ChatbotDental.Data;

/// <summary>
/// Guarda todo en memoria. Se usa cuando no hay DATABASE_URL configurada.
/// Los datos se pierden al reiniciar: sirve solo para desarrollo.
/// </summary>
public class RepositorioMemoria : IRepositorio
{
    private readonly ConcurrentDictionary<string, List<Mensaje>> _conversaciones = new();
    private readonly List<Solicitud> _solicitudes = new();
    private readonly object _lock = new();
    private long _proximoId = 1;

    public Task InicializarAsync() => Task.CompletedTask;

    public Task AgregarMensajeAsync(string conversacionId, string rol, string texto, string? ip)
    {
        var lista = _conversaciones.GetOrAdd(conversacionId, _ => new List<Mensaje>());
        lock (lista) lista.Add(new Mensaje(rol, texto, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Mensaje>> ObtenerMensajesAsync(string conversacionId, int ultimos)
    {
        if (!_conversaciones.TryGetValue(conversacionId, out var lista))
            return Task.FromResult<IReadOnlyList<Mensaje>>(Array.Empty<Mensaje>());
        lock (lista) return Task.FromResult<IReadOnlyList<Mensaje>>(lista.TakeLast(ultimos).ToList());
    }

    public Task<int> ContarMensajesUsuarioAsync(string conversacionId)
    {
        if (!_conversaciones.TryGetValue(conversacionId, out var lista)) return Task.FromResult(0);
        lock (lista) return Task.FromResult(lista.Count(m => m.Rol == "user"));
    }

    public Task<long> GuardarSolicitudAsync(NuevaSolicitud s)
    {
        lock (_lock)
        {
            var id = _proximoId++;
            _solicitudes.Add(new Solicitud(id, s.ConversacionId, s.Tipo, s.Nombre, s.Telefono,
                s.Detalle, s.FechaPreferida, s.Franja, DateTimeOffset.UtcNow));
            return Task.FromResult(id);
        }
    }

    public Task<IReadOnlyList<Solicitud>> ListarSolicitudesAsync(int limite)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<Solicitud>>(
                _solicitudes.OrderByDescending(s => s.Fecha).Take(limite).ToList());
    }

    public Task<IReadOnlyList<ResumenConversacion>> ListarConversacionesAsync(int limite)
    {
        var resumenes = _conversaciones
            .Select(par =>
            {
                List<Mensaje> copia;
                lock (par.Value) copia = par.Value.ToList();
                return (par.Key, copia);
            })
            .Where(x => x.copia.Count > 0)
            .Select(x => new ResumenConversacion(
                x.Key,
                x.copia.First().Fecha,
                x.copia.Last().Fecha,
                x.copia.Count,
                x.copia.FirstOrDefault(m => m.Rol == "user")?.Texto ?? ""))
            .OrderByDescending(r => r.UltimoMensaje)
            .Take(limite)
            .ToList();
        return Task.FromResult<IReadOnlyList<ResumenConversacion>>(resumenes);
    }

    public Task<IReadOnlyList<Mensaje>> ObtenerConversacionCompletaAsync(string conversacionId)
    {
        if (!_conversaciones.TryGetValue(conversacionId, out var lista))
            return Task.FromResult<IReadOnlyList<Mensaje>>(Array.Empty<Mensaje>());
        lock (lista) return Task.FromResult<IReadOnlyList<Mensaje>>(lista.ToList());
    }
}
