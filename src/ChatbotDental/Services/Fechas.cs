using System.Globalization;

namespace ChatbotDental.Services;

/// <summary>Fechas en hora de Argentina, para que "mañana" o "el jueves" se interpreten bien.</summary>
public static class Fechas
{
    private static readonly CultureInfo Es = new("es-AR");
    private static readonly TimeZoneInfo ZonaArgentina = BuscarZona();

    private static TimeZoneInfo BuscarZona()
    {
        foreach (var id in new[] { "America/Argentina/Buenos_Aires", "Argentina Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        // Argentina no usa horario de verano: UTC-3 fijo como respaldo.
        return TimeZoneInfo.CreateCustomTimeZone("AR", TimeSpan.FromHours(-3), "Argentina", "Argentina");
    }

    public static DateTime AhoraArgentina() =>
        TimeZoneInfo.ConvertTime(DateTime.UtcNow, ZonaArgentina);

    public static DateOnly HoyArgentina() => DateOnly.FromDateTime(AhoraArgentina());

    /// <summary>Ejemplo: "jueves 15 de octubre".</summary>
    public static string Legible(DateOnly dia) => dia.ToString("dddd d 'de' MMMM", Es);

    public static string AhoraLegible() =>
        AhoraArgentina().ToString("dddd d 'de' MMMM 'de' yyyy, HH:mm", Es);
}
