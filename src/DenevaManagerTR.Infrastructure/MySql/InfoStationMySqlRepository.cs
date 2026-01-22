using Dapper;
using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Infrastructure.InfoStation.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Data;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;

namespace DenevaManagerTR.Infrastructure.MySql;

public sealed class InfoStationMySqlRepository : IInfoStationRepository, ILineasEstadosRepository
{
    private readonly string _connectionString;
    private readonly ILogger<InfoStationMySqlRepository> _logger;
    private readonly IDenevaConfigReader _config;
    private readonly IConfiguration _appConfig;

    private const string Section = "TransIT.My.MySettings";
    private const string KeyDbName = "TRdbname";
    private const string KeyIp = "TRipbd";
    private const string KeyPort = "TRport";

    // Credenciales (requisito: hardcode)
    private const string User = "appuser";
    private const string Pass = "Lr4RaV1*C20Hd39";
    private bool _useMySql8;

    //public InfoStationMySqlRepository(string connectionString, ILogger<InfoStationMySqlRepository> logger)
    //{
    //    _connectionString = connectionString;
    //    _logger = logger;
    //}

    public InfoStationMySqlRepository(ILogger<InfoStationMySqlRepository> logger,IDenevaConfigReader config, IConfiguration appConfig)
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

    private async Task<MySqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = new MySqlConnection(BuildConnectionString());
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<InfoStationContext> GetContextAsync(string externalId, CancellationToken ct)
    {
        // 1) Vista: tv_dispositivos_presentacion (CLAVE: v_externalID sale de aquí)
        var deviceRows = await GetDevicePresentationRowsAsync(externalId, ct);

        // 2) Resolver idservidor (cuando la vista no devuelve datos)
        var serverId = deviceRows.FirstOrDefault()?.idservidor.ToString();
        if (string.IsNullOrWhiteSpace(serverId))
        {
            serverId = await GetServerIdFromDispositivoAsync(externalId, ct);
        }

        // 3) Servidor
        var servidor = !string.IsNullOrWhiteSpace(serverId)
            ? await GetServidorByIdAsync(serverId!, ct)
            : null;

        var isEmbarcado = servidor?.idServidores_Tipos == 3; // Heurística (ServidorEmbarcado). Ajustable.

        // 4) Idioma: se deja vacío si no está disponible (el consumer/service puede decidir fallback)
        var idiomaConsulta = string.Empty;

        // 5) Padre lógico y padre real
        var objIdPadre = await ObtenerObjIdPadreAsync(externalId, soloFisico: false, ct);
        var objIdPadreReal = await ObtenerObjIdPadreAsync(externalId, soloFisico: true, ct);

        // 6) OrigenRabbitMQ (en VB se construye con BuscarServidorFisico + '_' + padre)
        var origenRabbitMqBase = await BuscarServidorFisicoAsync(serverId, ct);

        // 7) Datos de player
        var denoExterna = await GetDenoExternaFromDispositivoAsync(externalId, ct);

        // 8) IdVia concatenado
        var idViaConcat = string.Join(";",
            deviceRows
                .Select(r => r.v_externalID)
                .Where(v => !string.IsNullOrWhiteSpace(v) && v != "0")
                .Distinct());

        // 9) Sistemas externos (SE)
        var sistemasExternos = await GetSistemasExternosAsync(serverId, ct);

        // 10) Presentaciones
        var presentaciones = await GetPresentacionesAsync(serverId, servidor?.EfectoInfoStation ?? string.Empty, ct);

        // 11) Lineas generales / estados / medios transporte / horarios
        var lineasGenerales = await GetLineasGeneralesAsync(ct);
        var lineasEstadosSeconds = await GetLineasEstadosSecondsAsync(ct);
        var lineasEstados = await GetLineasEstadosAsync(ct);
        var mediosTransporte = await GetMediosTransporteAsync(lineasGenerales, ct);
        var horarios = await GetHorariosAsync(ct);

        // 12) Lineas (detalle) + estaciones (detalle)
        // Se componen a partir de tv_dispositivos_presentacion y las tablas de trayectos/recorridos.
        IReadOnlyList<InfoStationLinea> lineas;
        IReadOnlyList<InfoStationEstacion> estaciones;
        var idViaConcatenado = string.Empty;
        var esEstacionTermino = false;

        if (deviceRows.Count == 0)
        {
            lineas = Array.Empty<InfoStationLinea>();
            estaciones = Array.Empty<InfoStationEstacion>();
        }
        else
        {
            idViaConcatenado = string.Join(";", deviceRows
                .Select(r => (r.v_externalID ?? string.Empty).Trim())
                .Where(v => !string.IsNullOrEmpty(v) && v != "0")
                .Distinct(StringComparer.OrdinalIgnoreCase));

            (lineas, estaciones, esEstacionTermino) = await BuildLineasAndEstacionesAsync(deviceRows, servidor?.Nemonico, ct);
        }

        return new InfoStationContext(
            ExternalId: externalId,
            ServerId: serverId ?? string.Empty,
            ServerNemonico: servidor?.Nemonico ?? string.Empty,
            ServerPcId: servidor?.IdPunto_Control ?? 0,
            ServerPresentacion: servidor?.Denominacion ?? string.Empty,
            IsEmbarcado: isEmbarcado,
            TrainMatricula: string.Empty,
            Efecto: servidor?.EfectoInfoStation ?? string.Empty,
            OrigenRabbitMq: origenRabbitMqBase,
            ObjIdPadre: objIdPadre.ToString(),
            ObjIdPadreReal: objIdPadreReal.ToString(),
            IdiomasPresentacion: string.Empty,
            IdiomasCodigoPresentacion: string.Empty,
            IdViaConcatenado: idViaConcatenado,
            EsEstacionTermino: esEstacionTermino,
            DenoExterna: denoExterna,
            SistemasExternos: sistemasExternos,
            Lineas: lineas,
            Estaciones: estaciones,
            LineasGenerales: lineasGenerales,
            LineasEstados: lineasEstados,
            MediosTransporte: mediosTransporte,
            Horarios: horarios,
            SettingsXml: string.Empty,
            Presentaciones: presentaciones
        );
    }

    private async Task<IReadOnlyList<InfoStationDeviceRow>> GetDevicePresentationRowsAsync(string externalId, CancellationToken ct)
    {
        const string sql = @"
SELECT 
  ExternalID,
  idservidor,
  idLinea,
  idTrayecto,
  s_nemonico,
  v_externalID,
  t_TrayectoCorto
FROM tv_dispositivos_presentacion
WHERE ExternalID = @ExternalID
  AND idservidor IS NOT NULL
GROUP BY idTrayecto, v_ID
ORDER BY idLinea, zdp_id, idTrayecto;";

        await using var conn = new MySqlConnection(BuildConnectionString());
        var rows = await conn.QueryAsync<InfoStationDeviceRow>(new CommandDefinition(sql, new { ExternalID = externalId }, cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<(IReadOnlyList<InfoStationLinea> Lineas, IReadOnlyList<InfoStationEstacion> Estaciones, bool EsEstacionTermino)> BuildLineasAndEstacionesAsync(
        IReadOnlyList<InfoStationDeviceRow> deviceRows,
        string? serverNemonico,
        CancellationToken ct)
    {
        // Nota: esta composición replica la estructura del VB (linea -> trayecto -> stops) usando la vista
        // tv_dispositivos_presentacion como fuente de idLinea/idTrayecto/v_externalID/t_TrayectoCorto.

        var trayectoIds = deviceRows
            .Where(r => r.idTrayecto.HasValue)
            .Select(r => r.idTrayecto!.Value)
            .Distinct()
            .ToArray();

        if (trayectoIds.Length == 0)
        {
            return (Array.Empty<InfoStationLinea>(), Array.Empty<InfoStationEstacion>(), false);
        }

        await using var conn = await OpenConnectionAsync(ct);

        // 1) Trayectos base
        var trayectos = (await conn.QueryAsync<TrayectoRow>(
            new CommandDefinition(
                "SELECT Id, Denominacion, PC_Destino FROM trayectos WHERE Id IN @Ids",
                new { Ids = trayectoIds },
                cancellationToken: ct)))
            .ToDictionary(t => t.Id);

        // 2) Nemonicos destino
        var pcDestinoIds = trayectos.Values
            .Where(t => t.PC_Destino.HasValue)
            .Select(t => t.PC_Destino!.Value)
            .Distinct()
            .ToArray();

        var pcsDestino = pcDestinoIds.Length == 0
            ? new Dictionary<int, string>()
            : (await conn.QueryAsync<PcRow>(
                new CommandDefinition(
                    "SELECT Id, Nemonico FROM puntos_control WHERE Id IN @Ids",
                    new { Ids = pcDestinoIds },
                    cancellationToken: ct)))
                .ToDictionary(p => p.Id, p => p.Nemonico ?? string.Empty);

        // 3) Stops por trayecto
        var stopRows = (await conn.QueryAsync<StopRow>(
            new CommandDefinition(
                @"SELECT 
                    tr.IdTrayecto,
                    tr.Orden,
                    tr.TiempoParada,
                    tr.TiempoSiguienteParada,
                    tr.idTrayectos_Ruta AS IdTrayectoRuta,
                    pc.Nemonico,
                    pc.Presentacion,
                    pc.AudioExternalID
                  FROM trayectos_recorrido tr
                  JOIN puntos_control pc ON pc.Id = tr.Idpunto_control
                  WHERE tr.IdTrayecto IN @Ids
                  ORDER BY tr.IdTrayecto, tr.Orden",
                new { Ids = trayectoIds },
                cancellationToken: ct)))
            .ToList();

        var rutaIds = stopRows
            .Where(s => s.IdTrayectoRuta.HasValue)
            .Select(s => s.IdTrayectoRuta!.Value)
            .Distinct()
            .ToArray();

        var rutas = rutaIds.Length == 0
            ? new Dictionary<int, string>()
            : (await conn.QueryAsync<RutaRow>(
                new CommandDefinition(
                    "SELECT Id, Denominacion FROM trayectos_ruta WHERE Id IN @Ids",
                    new { Ids = rutaIds },
                    cancellationToken: ct)))
                .ToDictionary(r => r.Id, r => r.Denominacion ?? string.Empty);

        var stopsByTrayecto = stopRows
            .GroupBy(s => s.IdTrayecto)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<InfoStationStop>)g.Select(s => new InfoStationStop(
                        NemoPuntoControl: s.Nemonico ?? string.Empty,
                        Orden: s.Orden,
                        TiempoParada: int.Parse(s.TiempoParada),
                        TiempoSiguienteParada: int.Parse(s.TiempoSiguienteParada),
                        TipoJourney: s.IdTrayectoRuta.HasValue && rutas.TryGetValue(s.IdTrayectoRuta.Value, out var denoRuta) ? denoRuta : string.Empty
                    ))
                    .ToList());

        // 4) Estaciones únicas (se alimentan de los stops)
        var estacionesByNemo = new Dictionary<string, InfoStationEstacion>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stopRows)
        {
            var nemo = s.Nemonico ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nemo) || estacionesByNemo.ContainsKey(nemo))
                continue;

            estacionesByNemo[nemo] = new InfoStationEstacion(
                NemoPuntoControl: nemo,
                DenominacionCData: s.Presentacion ?? string.Empty,
                AudioExternalId: s.AudioExternalID ?? 0,
                Prestaciones: Array.Empty<PrestacionEstacion>(),
                Correspondencias: Array.Empty<CorrespondenciaLinea>()
            );
        }

        // 5) Lineas -> trayectos
        var trayectoCortoById = deviceRows
            .Where(r => r.idTrayecto.HasValue)
            .GroupBy(r => r.idTrayecto!.Value)
            .ToDictionary(g => g.Key, g => g.Min(x => x.t_TrayectoCorto));

        var esEstacionTermino = false;

        var lineas = new List<InfoStationLinea>();
        foreach (var lineaGroup in deviceRows
                     .Where(r => r.idTrayecto.HasValue)
                     .GroupBy(r => r.idLinea)
                     .OrderBy(g => g.Key))
        {
            var trayectosLinea = new List<InfoStationTrayecto>();

            foreach (var trGroup in lineaGroup
                         .GroupBy(r => r.idTrayecto!.Value)
                         .OrderBy(g => g.Key))
            {
                var trayectoId = trGroup.Key;
                trayectos.TryGetValue(trayectoId, out var t);
                var firstRow = trGroup.First();

                var direccionNemo = string.Empty;
                if (t?.PC_Destino is int pcId && pcsDestino.TryGetValue(pcId, out var dn))
                    direccionNemo = dn;

                stopsByTrayecto.TryGetValue(trayectoId, out var stops);

                // Aproximación a "estación término": si el servidor coincide con el primer o último stop
                // de algún trayecto NO corto.
                if (!esEstacionTermino
                    && !string.IsNullOrWhiteSpace(serverNemonico)
                    && trayectoCortoById.TryGetValue(trayectoId, out var trCorto)
                    && trCorto == 0
                    && stops is { Count: > 0 })
                {
                    var firstN = stops[0].NemoPuntoControl ?? string.Empty;
                    var lastN = stops[^1].NemoPuntoControl ?? string.Empty;
                    if (serverNemonico.Equals(firstN, StringComparison.OrdinalIgnoreCase)
                        || serverNemonico.Equals(lastN, StringComparison.OrdinalIgnoreCase))
                    {
                        esEstacionTermino = true;
                    }
                }
                stops ??= Array.Empty<InfoStationStop>();

                trayectosLinea.Add(new InfoStationTrayecto(
                    IdTrayecto: trayectoId,
                    Denominacion: t?.Denominacion ?? string.Empty,
                    DireccionNemonico: direccionNemo,
                    Parcial: firstRow.t_TrayectoCorto,
                    IdVia: firstRow.v_externalID ?? string.Empty,
                    Stops: stops
                ));
            }

            lineas.Add(new InfoStationLinea(
                IdLinea: lineaGroup.Key,
                Trayectos: trayectosLinea
            ));
        }

        return (lineas, estacionesByNemo.Values.ToList(), esEstacionTermino);
    }

    private sealed class TrayectoRow
    {
        public int Id { get; set; }
        public string? Denominacion { get; set; }
        public int? PC_Destino { get; set; }
    }

    private sealed class PcRow
    {
        public int Id { get; set; }
        public string? Nemonico { get; set; }
    }

    private sealed class StopRow
    {
        public int IdTrayecto { get; set; }
        public int Orden { get; set; }
        public string? TiempoParada { get; set; }
        public string? TiempoSiguienteParada { get; set; }
        public int? IdTrayectoRuta { get; set; }
        public string? Nemonico { get; set; }
        public string? Presentacion { get; set; }
        public int? AudioExternalID { get; set; }
    }

    private sealed class RutaRow
    {
        public int Id { get; set; }
        public string? Denominacion { get; set; }
    }

    private async Task<string?> GetServerIdFromDispositivoAsync(string externalId, CancellationToken ct)
    {
        const string sql = @"SELECT idservidor FROM dispositivos WHERE ExternalID = @ExternalID LIMIT 1;";
        await using var conn = new MySqlConnection(BuildConnectionString());
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { ExternalID = externalId }, cancellationToken: ct));
    }

    private async Task<ServidorRow?> GetServidorByIdAsync(string serverId, CancellationToken ct)
    {
        const string sql = @"
SELECT 
  idservidores,
  ExternalID,
  Nemonico,
  Denominacion,
  idServidores_Tipos,
  isVirtual,
  Vinculo,
  IdPunto_Control,
  EfectoInfoStation
FROM servidores
WHERE idservidores = @Id
LIMIT 1;";

        await using var conn = new MySqlConnection(BuildConnectionString());
        return await conn.QueryFirstOrDefaultAsync<ServidorRow>(new CommandDefinition(sql, new { Id = serverId }, cancellationToken: ct));
    }

    private async Task<string> GetDenoExternaFromDispositivoAsync(string externalId, CancellationToken ct)
    {
        const string sql = @"SELECT DenoExternalID FROM dispositivos WHERE ExternalID = @ExternalID LIMIT 1;";
        await using var conn = new MySqlConnection(BuildConnectionString());
        return (await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new { ExternalID = externalId }, cancellationToken: ct))) ?? string.Empty;
    }

    private async Task<int> ObtenerObjIdPadreAsync(string externalId, bool soloFisico, CancellationToken ct)
    {
        // Replica de la lógica VB:
        // - Si es dispositivo: cuelga de su servidor.
        // - Si es servidor: se usa él.
        // - Para "padre" (no real): si servidor isVirtual=1 y tiene vínculo -> se usa el ExternalID del servidor vínculo.

        var serverId = await GetServerIdFromDispositivoAsync(externalId, ct);
        ServidorRow? servidor = null;

        if (!string.IsNullOrWhiteSpace(serverId))
        {
            servidor = await GetServidorByIdAsync(serverId!, ct);
        }
        else
        {
            // Puede venir un ExternalID de servidor
            const string sql = @"SELECT 
  Id,
  ExternalID,
  Nemonico,
  Denominacion,
  idServidores_Tipos,
  isVirtual,
  Vinculo,
  IdPunto_Control,
  EfectoInfoStation
FROM servidores
WHERE ExternalID = @ExternalID
LIMIT 1;";
            await using var conn = new MySqlConnection(BuildConnectionString());
            servidor = await conn.QueryFirstOrDefaultAsync<ServidorRow>(new CommandDefinition(sql, new { ExternalID = externalId }, cancellationToken: ct));
        }

        if (servidor is null)
            return 0;

        if (soloFisico)
            return servidor.ExternalID??0;

        if (servidor.isVirtual == 1)
        {
            if (servidor.Vinculo is null || servidor.Vinculo == servidor.Id)
                return servidor.ExternalID ?? 0;

            var vinc = await GetServidorByIdAsync(servidor.Vinculo.Value.ToString(), ct);
            return (int)(vinc?.ExternalID ?? servidor.ExternalID);
        }

        return (int)servidor.ExternalID;
    }

    private async Task<string> BuscarServidorFisicoAsync(string? serverId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serverId))
            return string.Empty;

        var s = await GetServidorByIdAsync(serverId!, ct);
        if (s is null)
            return string.Empty;

        if (s.isVirtual == 1 && s.Vinculo is not null && s.Vinculo != s.Id)
        {
            var vinc = await GetServidorByIdAsync(s.Vinculo.Value.ToString(), ct);
            return "Servidor_" + vinc?.ExternalID;
        }
        else
        {
            return "Bastion_" + s?.ExternalID;
        }

        return s.Nemonico;
    }

    private async Task<IReadOnlyList<int>> GetSistemasExternosAsync(string? serverId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serverId))
            return Array.Empty<int>();

        const string sql = @"
SELECT DISTINCT l.idsistemas_externos
FROM tv_recorrido r
INNER JOIN lineas l ON l.idLineas = r.idLineas
WHERE r.IdServidores = @IdServidores
  AND l.idsistemas_externos IS NOT NULL;";

        await using var conn = new MySqlConnection(BuildConnectionString());
        var rows = await conn.QueryAsync<int?>(new CommandDefinition(sql, new { IdServidores = serverId }, cancellationToken: ct));
        return rows.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
    }

    private async Task<PresentacionesInfoStation> GetPresentacionesAsync(string? serverId, string efecto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serverId))
            return new PresentacionesInfoStation(efecto, Array.Empty<PresentacionInfo>());

        const string sql = @"
SELECT TiempoPresentacion as Tiempo, Presentacion
FROM servidores_infostation
WHERE idServidor = @IdServidor
ORDER BY Orden, idServidor;";

        await using var conn = new MySqlConnection(BuildConnectionString());
        var items = await conn.QueryAsync<PresentacionInfo>(new CommandDefinition(sql, new { IdServidor = serverId }, cancellationToken: ct));
        return new PresentacionesInfoStation(efecto, items.ToList());
    }

    private async Task<IReadOnlyList<LineaGeneral>> GetLineasGeneralesAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT 
  idLineas,
  Identificador,
  IdMedio_transporte as IdMedioTransporte,
  CONCAT('0x', Color) as ColorHex,
  CONCAT('0x', ColorLetra) as ColorLetraHex,
  0 as EstadoDefault,
  ModoPresentacionPaneles,
  OrdenVisualLineasEstado as OrdenVisualLineasEstados,
  IFNULL(LogotipoExternalID, 0) as LogotipoExternalId,
  IFNULL(AudioExternalID, 0) as AudioExternalId
FROM lineas
ORDER BY OrdenVisualLineasEstado, idLineas;";

        await using var conn = new MySqlConnection(BuildConnectionString());
        var rows = await conn.QueryAsync<LineaGeneral>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> GetLineasEstadosSecondsAsync(CancellationToken ct)
    {
        // No hay una tabla clara en este dump para settings; se deja un valor razonable por defecto.
        await Task.CompletedTask;
        return 10;
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

    private async Task<IReadOnlyList<MedioTransporte>> GetMediosTransporteAsync(IReadOnlyList<LineaGeneral> lineasGenerales, CancellationToken ct)
    {
        var ids = lineasGenerales.Select(l => l.IdMedioTransporte).Distinct().ToArray();
        if (ids.Length == 0)
            return Array.Empty<MedioTransporte>();

        const string sql = @"
SELECT 
  idMedios_transporte,
  IFNULL(Denominacion, '') as Denominacion,
  IFNULL(Presentacion, '') as Presentacion,
'0' as LogotipoExternalId,
'0' as LogotipoAuxExternalId
FROM medios_transporte
WHERE idMedios_transporte IN @Ids
ORDER BY idMedios_transporte;";

        await using var conn = new MySqlConnection(BuildConnectionString());
        var rows = await conn.QueryAsync<MedioTransporte>(new CommandDefinition(sql, new { Ids = ids }, cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<IReadOnlyList<HorarioTarifa>> GetHorariosAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT 
  idhorarios_tarifas,
  CONCAT('#', Color) as Color,
  CONCAT('#', ColorLetra) as ColorLetra,
  IFNULL(Presentacion, '') as Presentacion
FROM horarios_tarifas
ORDER BY idhorarios_tarifas;";

        await using var conn = new MySqlConnection(BuildConnectionString());
        var rows = await conn.QueryAsync<HorarioTarifa>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<string> GetInfoLineasEstadosAsync(int empresa,string DirectorioRecursos, CancellationToken ct)
    {
        // Nota: Esto replica “estructura” del VB, pero simplificado.
        // Tú lo irás enriqueciendo con playlist/logos/medios transporte etc.

        var now = DateTime.Now;
        var caducidad = now.AddMinutes(5);//sacar de setting
        var dirRecursos = (DirectorioRecursos ?? string.Empty).Replace("\\", "/");

        var root = new XElement("datasource",
            new XAttribute("Timestamp", now.ToString("yyyy-MM-dd HH:mm:ss")),
            new XAttribute("caducidad", caducidad.ToString("yyyy-MM-dd HH:mm:ss")),
            new XAttribute("ruta_recursos", dirRecursos)
        );

        // Para replicar el “xmlns:json” del legacy:
        // <datasource xmlns:json='http://james.newtonking.com/projects/json' ...>
        XNamespace jsonNs = "http://james.newtonking.com/projects/json";
        root.Add(new XAttribute(XNamespace.Xmlns + "json", jsonNs));

        // 1) lineas_estados
        var lineasEstados = new XElement("lineas_estados");

        // 2) lineas_generales (de momento lo dejamos vacío si no tienes playlist)
        var lineasGenerales = new XElement("lineas_generales");

        // 3) medios_transporte (de momento vacío)
        var mediosTransporte = new XElement("medios_transporte");

        // Carga desde BD: itv_lineas_estado (según tu SQL real, ajusta nombres)
        // IMPORTANTE: yo no puedo adivinar el esquema exacto; por eso:
        // - consulto de forma “tolerante” y logueo si falla
        // - tú adaptarás el SELECT a tu transit.sql
        try
        {
            await using var conn = new MySqlConnection(BuildConnectionString());
            await conn.OpenAsync(ct);

            // Ajusta este SELECT a tus tablas reales (transit.sql).
            // En legacy: GetByEmpresa(...) ordenado por L_OrdenVisual.
            const string sql = @"
                SELECT
                    idLineas,
                    L_Estado,
                    LE_Color,
                    LE_ColorLetra,
                    LE_Prioridad,
                    E_Titular,
                    E_Descripcion,
                    L_OrdenVisual
                FROM tv_lineas_estado
                WHERE L_Empresa = @empresa
                ORDER BY L_OrdenVisual;";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.Add("@empresa", MySqlDbType.Int32).Value = empresa;

            await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

            while (await rdr.ReadAsync(ct))
            {
                // Manejo null-safe
                var id = rdr["idLineas"] != DBNull.Value ? Convert.ToInt32(rdr["idLineas"]) : 0;
                var estadoId = rdr["L_Estado"] != DBNull.Value ? Convert.ToInt32(rdr["L_Estado"]) : 0;

                var color = rdr["LE_Color"] != DBNull.Value ? Convert.ToString(rdr["LE_Color"]) : "000000";
                var colorLetra = rdr["LE_ColorLetra"] != DBNull.Value ? Convert.ToString(rdr["LE_ColorLetra"]) : "FFFFFF";
                var prioridad = rdr["LE_Prioridad"] != DBNull.Value ? Convert.ToInt32(rdr["LE_Prioridad"]) : 0;

                var titular = rdr["E_Titular"] != DBNull.Value ? Convert.ToString(rdr["E_Titular"]) : string.Empty;
                var descripcion = rdr["E_Descripcion"] != DBNull.Value ? Convert.ToString(rdr["E_Descripcion"]) : string.Empty;

                // En VB: <linea_estado json:Array='true' ... bgColor="0xXXXX" fgColor="0xXXXX" ...>
                var lineaEstado = new XElement("linea_estado",
                    new XAttribute(jsonNs + "Array", "true"),
                    new XAttribute("linea_id", id),
                    new XAttribute("estado_id", estadoId),
                    new XAttribute("bgColor", "0x" + (color ?? "000000")),
                    new XAttribute("fgColor", "0x" + (colorLetra ?? "FFFFFF")),
                    new XAttribute("prioridad", prioridad)
                );

                lineaEstado.Add(new XElement("title", titular ?? string.Empty));
                lineaEstado.Add(new XElement("description", descripcion ?? string.Empty));

                lineasEstados.Add(lineaEstado);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo tv_lineas_estado en MySQL. empresa={Empresa}", empresa);

            // Si falla BD, devolvemos XML válido pero sin datos (para no romper el flujo Rabbit)
            // Alternativa: re-lanzar y que OnMessageReceivedAsync no publique.
        }

        root.Add(lineasEstados);
        root.Add(lineasGenerales);
        root.Add(mediosTransporte);

        // Serializar sin indent para que sea más “legacy-friendly”
        var xml = root.ToString(SaveOptions.DisableFormatting);

        _logger.LogDebug("GetInfoLineasEstadosAsync generado. empresa={Empresa} bytes={Bytes}", empresa, Encoding.UTF8.GetByteCount(xml));
        return xml;
    }

}
