using System.Collections.Concurrent;

namespace ChatbotDental.Services;

public class OpcionesLimite
{
    /// <summary>Mensajes máximos por conversación.</summary>
    public int MensajesPorConversacion { get; set; } = 25;
    /// <summary>Mensajes máximos por IP en 24 horas.</summary>
    public int MensajesPorIpPorDia { get; set; } = 60;
    /// <summary>Tope global de mensajes por día (protege el gasto en la API).</summary>
    public int MensajesTotalesPorDia { get; set; } = 1500;
    /// <summary>Largo máximo de cada mensaje del usuario.</summary>
    public int LargoMaximo { get; set; } = 600;
}

/// <summary>
/// Límites de uso en memoria. Como es un demo público, evitan que alguien
/// consuma todo el crédito de la API mandando mensajes sin parar.
/// </summary>
public class LimiteUso(OpcionesLimite opciones)
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _porIp = new();
    private readonly object _lockGlobal = new();
    private DateOnly _diaGlobal = DateOnly.FromDateTime(DateTime.UtcNow);
    private int _totalHoy;

    public OpcionesLimite Opciones => opciones;

    /// <summary>Devuelve null si se permite, o el motivo si se bloquea.</summary>
    public string? Verificar(string ip)
    {
        var ahora = DateTime.UtcNow;

        lock (_lockGlobal)
        {
            var hoy = DateOnly.FromDateTime(ahora);
            if (hoy != _diaGlobal) { _diaGlobal = hoy; _totalHoy = 0; }
            if (_totalHoy >= opciones.MensajesTotalesPorDia)
                return "El demo alcanzó el límite de mensajes de hoy. Volvé a probar mañana.";
        }

        var cola = _porIp.GetOrAdd(ip, _ => new ConcurrentQueue<DateTime>());
        while (cola.TryPeek(out var primero) && ahora - primero > TimeSpan.FromHours(24))
            cola.TryDequeue(out _);
        if (cola.Count >= opciones.MensajesPorIpPorDia)
            return "Llegaste al límite de mensajes del demo por hoy. Volvé a probar mañana.";

        cola.Enqueue(ahora);
        lock (_lockGlobal) _totalHoy++;
        return null;
    }
}
