using Dapper;
using DenevaManagerTR.Core.Domain.Heartbeat;
using DenevaManagerTR.Core.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DenevaManagerTR.Infrastructure.MySql;

public sealed class TransitMySqlRepository : ITransitRepository
{
    private readonly ILogger<TransitMySqlRepository> _logger;
    private readonly IDenevaConfigReader _config;

    private const string Section = "TransIT.My.MySettings";
    private const string KeyDbName = "TRdbname";
    private const string KeyIp = "TRipbd";
    private const string KeyPort = "TRport";

    // Credenciales (requisito: hardcode)
    private const string User = "appuser";
    private const string Pass = "Lr4RaV1*C20Hd39";
    private bool _useMySql8;

    public TransitMySqlRepository(ILogger<TransitMySqlRepository> logger, IDenevaConfigReader config, IConfiguration appConfig)
    {
        _logger = logger;
        _config = config;
        _useMySql8 = string.Equals(appConfig["Database:Engine"], "mysql8", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildConnectionString()
    {
        var db = _config.Get(Section, KeyDbName) ?? "";
        var host = _config.Get(Section, KeyIp) ?? "localhost";
        var portStr = _config.Get(Section, KeyPort) ?? "3306";
        if (!uint.TryParse(portStr, out var port)) port = 3306;

        var csb = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = port,
            Database = db,
            UserID = User,
            Password = Pass,
            SslMode = MySqlSslMode.None,
            // Ajustes defensivos
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 10,
            // Recomendable
            AllowUserVariables = true,
            TreatTinyAsBoolean = false,
            Pooling = true
        };

        if (_useMySql8)
        {
            // Muy habitual para MySQL 8 con caching_sha2_password sin SSL
            csb.AllowPublicKeyRetrieval = true;

            // Opcional: algunos entornos lo agradecen
            csb.CharacterSet = "utf8mb4";
        }
        return csb.ConnectionString;
    }

    public async Task<long> GetLastUpdateMsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            await using var conn = new MySqlConnection(BuildConnectionString());
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT `Timestamp` FROM transact_tables WHERE Tabla = @t LIMIT 1;";
            cmd.Parameters.AddWithValue("@t", tableName);

            var obj = await cmd.ExecuteScalarAsync(cancellationToken);
            if (obj is null || obj is DBNull)
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // MySqlConnector devuelve DateTime para DATETIME/TIMESTAMP
            if (obj is DateTime dt)
            {
                // Asumimos hora LOCAL (ajustable si se confirma UTC)
                var dto = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local));
                return dto.ToUnixTimeMilliseconds();
            }

            // Si viene string
            if (obj is string s && DateTime.TryParse(s, out var dts))
            {
                var dto = new DateTimeOffset(DateTime.SpecifyKind(dts, DateTimeKind.Local));
                return dto.ToUnixTimeMilliseconds();
            }

            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo obtener LastUpdate desde MySQL (tabla={Table}). Se usa ahora.", tableName);
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
    public async Task<IReadOnlyList<SseEntry>> GetHeartbeatSeEntriesAsync(CancellationToken cancellationToken = default)
    {
        // 1) Determinar si hay sistemas externos de seguimiento habilitados (SE.Enabled=1 e IsSeguimiento=1)
        //    Nota: no dependemos de una vista concreta; lo resolvemos con joins equivalentes.
        var ids = new List<int>();

        try
        {
            await using var conn = new MySqlConnection(BuildConnectionString());
            await conn.OpenAsync(cancellationToken);

            // Este query reproduce la condición que indicaste: SE_Enabled=1 y setipo.IsSeguimiento=1
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT DISTINCT se.idsistemas_externos
FROM servidores s
JOIN vias v ON s.idservidores = v.idservidor
JOIN servidores_trayectos_vias stv ON stv.idvia = v.idvias
JOIN trayectos t ON stv.idTrayecto = t.idTrayectos
JOIN lineas l ON l.idLineas = t.idLinea
JOIN sistemas_externos se ON l.idsistemas_externos = se.idsistemas_externos
JOIN servidores_sistemas_externos sse ON se.idsistemas_externos = sse.idsistema_externo
JOIN sistemas_externos_protocolo sep ON sep.idsistemas_externos_protocolo = se.idsistemas_externos_protocolo
JOIN sistemas_externos_tipos setipo ON setipo.idsistemas_externos_tipos = sep.idsistemas_externos_tipos
WHERE se.Enabled = 1 AND setipo.IsSeguimiento = 1
ORDER BY se.idsistemas_externos;
";
                await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await rdr.ReadAsync(cancellationToken))
                {
                    if (!rdr.IsDBNull(0))
                        ids.Add(rdr.GetInt32(0));
                }
            }

            // 2) Consulta de SSE según regla: por idsistema_externo si hay ids; si no, todos los Enabled=1
            var result = new List<SseEntry>();

            await using (var cmd2 = conn.CreateCommand())
            {
                if (ids.Count > 0)
                {
                    // IN dinámico
                    var pNames = new List<string>();
                    for (var i = 0; i < ids.Count; i++)
                    {
                        var pn = "@id" + i;
                        pNames.Add(pn);
                        cmd2.Parameters.AddWithValue(pn, ids[i]);
                    }

                    cmd2.CommandText = $@"
SELECT idservidores_sistemas_externos, IFNULL(idmodos_seguimiento, 1), IFNULL(idmodos_trabajo, 2), IFNULL(Omitir, 0)
FROM servidores_sistemas_externos
WHERE idsistema_externo IN ({string.Join(",", pNames)})";
                }
                else
                {
                    cmd2.CommandText = @"
SELECT idservidores_sistemas_externos, IFNULL(idmodos_seguimiento, 1), IFNULL(idmodos_trabajo, 2), IFNULL(Omitir, 0)
FROM servidores_sistemas_externos
WHERE Enabled = 1";
                }

                await using var rdr2 = await cmd2.ExecuteReaderAsync(cancellationToken);
                while (await rdr2.ReadAsync(cancellationToken))
                {
                    var id = rdr2.IsDBNull(0) ? 0 : rdr2.GetInt32(0);
                    var ms = rdr2.IsDBNull(1) ? 1 : rdr2.GetInt32(1);
                    var mt = rdr2.IsDBNull(2) ? 2 : rdr2.GetInt32(2);
                    var om = rdr2.IsDBNull(3) ? 0 : rdr2.GetInt32(3);

                    if (id > 0)
                        result.Add(new SseEntry(id, ms, mt, om));
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo obtener la lista de SSE para el HeartBeat. Se omite <SE>.");
            return Array.Empty<SseEntry>();
        }
    }
        public async Task<int> GetLineasEstadosSecondsAsync(CancellationToken ct)
    {
        await Task.CompletedTask;
        return 10; // o léelo de deneva.config si quieres
    }

    public async Task<IReadOnlyList<LineaEstado>> GetLineasEstadosAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT 
  idlineas_estados,
  CONCAT('#', Color) as Color,
  CONCAT('#', ColorLetra) as ColorLetra,
  IFNULL(Descripcion, '') as Descripcion
FROM lineas_estados
ORDER BY idlineas_estados;";

        await using var conn = new MySqlConnection(BuildConnectionString());
        var rows = await conn.QueryAsync<LineaEstado>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

}
