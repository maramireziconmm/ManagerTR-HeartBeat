using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DenevaManagerTR.Core.Ports;

/// <summary>
/// Acceso a datos (MySQL) para construir el XML de InfoStation.
/// Se diseña como capa de persistencia para poder evolucionar la generación del XML sin acoplarla al consumer.
/// </summary>
public interface IInfoStationRepository
{
    Task<InfoStationContext> GetContextAsync(string externalId, CancellationToken ct);
}

/// <summary>
/// DTO agregado con los datos mínimos para componer el XML. Se irá ampliando.
/// </summary>
public sealed record InfoStationContext(
    string ExternalId,
    string? ServerId,
    string? ServerNemonico,
    int? ServerPcId,
    string? ServerPresentacion,
    bool IsEmbarcado,
    string? TrainMatricula,
    string? Efecto,
    string? OrigenRabbitMq,
    string? ObjIdPadre,
    string? ObjIdPadreReal,
    string? IdViaConcatenado,
    bool EsEstacionTermino,
    string IdiomasPresentacion,
    string IdiomasCodigoPresentacion,
    string? DenoExterna,
    IReadOnlyList<int> SistemasExternos,
    IReadOnlyList<InfoStationLinea> Lineas,
    IReadOnlyList<InfoStationEstacion> Estaciones,
    IReadOnlyList<LineaGeneral> LineasGenerales,
    IReadOnlyList<LineaEstado> LineasEstados,
    IReadOnlyList<MedioTransporte> MediosTransporte,
    IReadOnlyList<HorarioTarifa> Horarios,
    string SettingsXml,
    PresentacionesInfoStation Presentaciones
);

public sealed record InfoStationLinea(int IdLinea, IReadOnlyList<InfoStationTrayecto> Trayectos);

public sealed record InfoStationTrayecto(
    int IdTrayecto,
    string? Denominacion,
    string? DireccionNemonico,
    int Parcial,
    string? IdVia,
    IReadOnlyList<InfoStationStop> Stops
);

public sealed record InfoStationStop(
    string NemoPuntoControl,
    int Orden,
    int? TiempoParada,
    int? TiempoSiguienteParada,
    string? TipoJourney
);

public sealed record InfoStationEstacion(
    string NemoPuntoControl,
    string? DenominacionCData,
    int? AudioExternalId,
    IReadOnlyList<PrestacionEstacion> Prestaciones,
    IReadOnlyList<CorrespondenciaLinea> Correspondencias
);

public sealed record PrestacionEstacion(string UrlServicio);

public sealed record CorrespondenciaLinea(string UrlImg, string Deno, string BgColorHex);

public sealed class LineaGeneral
{
    // Dapper necesita ctor público sin parámetros para setear props
    public LineaGeneral() { }

    public int idLineas { get; set; }
    public string Identificador { get; set; } = "";

    public int IdMedioTransporte { get; set; }

    public string ColorHex { get; set; } = "";
    public string ColorLetraHex { get; set; } = "";

    // OJO: MySQL suele devolverte esto como Int64
    public long EstadoDefault { get; set; }

    // tinyint/smallint suelen mapear bien a sbyte/byte/int; si te da guerra, pon int.
    public sbyte ModoPresentacionPaneles { get; set; }

    public int OrdenVisualLineasEstados { get; set; }

    public long LogotipoExternalId { get; set; }
    public long AudioExternalId { get; set; }
}

public sealed record LineaEstado(int idlineas_estados, string Color, string ColorLetra, string Descripcion);

public sealed class MedioTransporte
{
    // Dapper necesita ctor público sin parámetros para setear props
    public MedioTransporte() { }

    public int idMedios_transporte { get; set; }
    public string Denominacion { get; set; } = "";
    public string Presentacion { get; set; } = "";
    public string LogotipoExternalId { get; set; }
    public string LogotipoAuxExternalId { get; set; }

}
public sealed class HorarioTarifa
{
    // Dapper necesita ctor público sin parámetros para setear props
    public HorarioTarifa() { }

    public int idhorarios_tarifas { get; set; }
    public string Presentacion { get; set; } = "";
    public string Color { get; set; }
    public string ColorLetra { get; set; }

}


public sealed record PresentacionesInfoStation(string Efecto, IReadOnlyList<PresentacionInfo> Items);

public sealed record PresentacionInfo(int Tiempo, string Presentacion);
