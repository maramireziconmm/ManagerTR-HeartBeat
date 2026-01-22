namespace DenevaManagerTR.Infrastructure.InfoStation.Models;

/// <summary>
/// Fila de la vista tv_dispositivos_presentacion.
/// Debe ser public para evitar incoherencias de accesibilidad si se expone desde métodos públicos.
/// </summary>
public sealed class InfoStationDeviceRow
{
    public string ExternalID { get; set; } = "";
    public int idservidor { get; set; }

    public int idLinea { get; set; }
    public int? idTrayecto { get; set; }

    public string s_nemonico { get; set; } = "";

    // CLAVE: v_externalID viene de la vista tv_dispositivos_presentacion.
    public string v_externalID { get; set; } = "";

    public int t_TrayectoCorto { get; set; }
}

public sealed class ServidorRow
{
    public int Id { get; set; }
    public string Nemonico { get; set; } = "";
    public string Denominacion { get; set; } = "";
    public int? ExternalID { get; set; }
    public int? IdPunto_Control { get; set; }
    public int? idServidores_Tipos { get; set; }
    public int? isVirtual { get; set; }
    public int? Vinculo { get; set; }
    public string? EfectoInfoStation { get; set; }
}

public sealed class ServidorIdiomaRow
{
    public string NemonicoIdioma { get; set; } = "";
}

public sealed class IdiomaEmpresaRow
{
    public string NemonicoIdioma { get; set; } = "";
}

public sealed class SistemaExternoLineaRow
{
    public int IdSistemaExterno { get; set; }
}
