using Npgsql;
using NpgsqlTypes;

namespace ChatbotDental.Data;

/// <summary>
/// Repositorio sobre PostgreSQL usando Npgsql directo (sin ORM),
/// para que las consultas SQL queden a la vista y sean fáciles de seguir.
/// </summary>
public class RepositorioPostgres : IRepositorio
{
    private readonly NpgsqlDataSource _db;

    public RepositorioPostgres(string connectionString)
    {
        _db = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>
    /// Railway entrega la base como URL (postgresql://usuario:clave@host:puerto/base).
    /// Npgsql necesita el formato "Host=...;Username=...", así que la convertimos.
    /// </summary>
    public static string DesdeUrl(string valor)
    {
        if (!valor.StartsWith("postgres://") && !valor.StartsWith("postgresql://"))
            return valor; // ya viene en formato Npgsql

        var uri = new Uri(valor);
        var credenciales = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(credenciales[0]),
            Password = credenciales.Length > 1 ? Uri.UnescapeDataString(credenciales[1]) : "",
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Prefer,
        };
        return builder.ConnectionString;
    }

    public async Task InicializarAsync()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS mensajes (
                id              BIGSERIAL PRIMARY KEY,
                conversacion_id TEXT        NOT NULL,
                rol             TEXT        NOT NULL,
                texto           TEXT        NOT NULL,
                ip              TEXT,
                fecha           TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_mensajes_conversacion ON mensajes (conversacion_id, id);

            CREATE TABLE IF NOT EXISTS solicitudes (
                id              BIGSERIAL PRIMARY KEY,
                conversacion_id TEXT        NOT NULL,
                tipo            TEXT        NOT NULL,
                nombre          TEXT        NOT NULL,
                telefono        TEXT        NOT NULL,
                detalle         TEXT        NOT NULL,
                fecha_preferida TEXT,
                franja          TEXT,
                fecha           TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """;
        await using var cmd = _db.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AgregarMensajeAsync(string conversacionId, string rol, string texto, string? ip)
    {
        await using var cmd = _db.CreateCommand(
            "INSERT INTO mensajes (conversacion_id, rol, texto, ip) VALUES ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(conversacionId);
        cmd.Parameters.AddWithValue(rol);
        cmd.Parameters.AddWithValue(texto);
        cmd.Parameters.Add(TextoNulable(ip));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Mensaje>> ObtenerMensajesAsync(string conversacionId, int ultimos)
    {
        await using var cmd = _db.CreateCommand("""
            SELECT rol, texto, fecha FROM (
                SELECT id, rol, texto, fecha FROM mensajes
                WHERE conversacion_id = $1
                ORDER BY id DESC LIMIT $2
            ) t ORDER BY id
            """);
        cmd.Parameters.AddWithValue(conversacionId);
        cmd.Parameters.AddWithValue(ultimos);
        return await LeerMensajesAsync(cmd);
    }

    public async Task<int> ContarMensajesUsuarioAsync(string conversacionId)
    {
        await using var cmd = _db.CreateCommand(
            "SELECT COUNT(*) FROM mensajes WHERE conversacion_id = $1 AND rol = 'user'");
        cmd.Parameters.AddWithValue(conversacionId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<long> GuardarSolicitudAsync(NuevaSolicitud s)
    {
        await using var cmd = _db.CreateCommand("""
            INSERT INTO solicitudes (conversacion_id, tipo, nombre, telefono, detalle, fecha_preferida, franja)
            VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING id
            """);
        cmd.Parameters.AddWithValue(s.ConversacionId);
        cmd.Parameters.AddWithValue(s.Tipo);
        cmd.Parameters.AddWithValue(s.Nombre);
        cmd.Parameters.AddWithValue(s.Telefono);
        cmd.Parameters.AddWithValue(s.Detalle);
        cmd.Parameters.Add(TextoNulable(s.FechaPreferida));
        cmd.Parameters.Add(TextoNulable(s.Franja));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<Solicitud>> ListarSolicitudesAsync(int limite)
    {
        await using var cmd = _db.CreateCommand("""
            SELECT id, conversacion_id, tipo, nombre, telefono, detalle, fecha_preferida, franja, fecha
            FROM solicitudes ORDER BY id DESC LIMIT $1
            """);
        cmd.Parameters.AddWithValue(limite);
        var lista = new List<Solicitud>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            lista.Add(new Solicitud(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.GetFieldValue<DateTimeOffset>(8)));
        }
        return lista;
    }

    public async Task<IReadOnlyList<ResumenConversacion>> ListarConversacionesAsync(int limite)
    {
        await using var cmd = _db.CreateCommand("""
            SELECT conversacion_id,
                   MIN(fecha) AS inicio,
                   MAX(fecha) AS ultimo,
                   COUNT(*)   AS cantidad,
                   (SELECT texto FROM mensajes m2
                     WHERE m2.conversacion_id = m.conversacion_id AND m2.rol = 'user'
                     ORDER BY id LIMIT 1) AS primero
            FROM mensajes m
            GROUP BY conversacion_id
            ORDER BY ultimo DESC
            LIMIT $1
            """);
        cmd.Parameters.AddWithValue(limite);
        var lista = new List<ResumenConversacion>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            lista.Add(new ResumenConversacion(
                r.GetString(0),
                r.GetFieldValue<DateTimeOffset>(1),
                r.GetFieldValue<DateTimeOffset>(2),
                Convert.ToInt32(r.GetInt64(3)),
                r.IsDBNull(4) ? "" : r.GetString(4)));
        }
        return lista;
    }

    public async Task<IReadOnlyList<Mensaje>> ObtenerConversacionCompletaAsync(string conversacionId)
    {
        await using var cmd = _db.CreateCommand(
            "SELECT rol, texto, fecha FROM mensajes WHERE conversacion_id = $1 ORDER BY id");
        cmd.Parameters.AddWithValue(conversacionId);
        return await LeerMensajesAsync(cmd);
    }

    private static NpgsqlParameter TextoNulable(string? valor) =>
        new() { Value = (object?)valor ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text };

    private static async Task<IReadOnlyList<Mensaje>> LeerMensajesAsync(NpgsqlCommand cmd)
    {
        var lista = new List<Mensaje>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lista.Add(new Mensaje(r.GetString(0), r.GetString(1), r.GetFieldValue<DateTimeOffset>(2)));
        return lista;
    }
}
