using DenevaManagerTR.Core.Ports;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace DenevaManagerTR.Application.InfoStation;

public sealed class InfoStationService : IInfoStationService
{
    private readonly IInfoStationRepository _repo;

    // En el VB se lee de settings: Transit_tiempo_estados_presentacion_idiomas (default 10)
    private const int DefaultLineasEstadosSeconds = 10;

    public InfoStationService(IInfoStationRepository repo)
    {
        _repo = repo;
    }

    public async Task<string> GetInformacionEstacionAsync(string externalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            return string.Empty;

        var ctx = await _repo.GetContextAsync(externalId.Trim(), cancellationToken);

        // idVia concatenado (en VB se forma con v_externalID de tv_dispositivos_presentacion)
        // Aquí lo derivamos de los trayectos recuperados.
        var idViaConcatenado = BuildIdViaConcatenado(ctx);

        // En la versión .NET actual no tenemos (aún) TipoRecorrido en stop, así que no podemos reproducir
        // el cálculo exacto de "estacionTermino". Mantengo 0.
        var estacionTermino = "0";

        var sb = new StringBuilder(32_768);
        sb.Append("<?xml version='1.0' encoding='iso-8859-1' ?> ");
        sb.Append("<datasource Timestamp='");
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.Append("' origenRabbitMQ='");
        sb.Append(XmlEscapeAttr($"{ctx.OrigenRabbitMq}"));
        sb.Append("'  origenRabbitMQ_padre='");
        sb.Append(XmlEscapeAttr(ctx.ObjIdPadreReal));
        sb.Append("' >");

        // train / estacion
        if (ctx.IsEmbarcado)
        {
            // En VB usa matricula de vehiculos; no está disponible en este repositorio.
            sb.Append("<train id='");
            sb.Append("'");
            sb.Append(" idPCEstacion='");
            sb.Append(ctx.ServerPcId);
            sb.Append("' idiomas_presentacion='");
            sb.Append(XmlEscapeAttr(ctx.IdiomasPresentacion));
            sb.Append("' idiomas_codigo_presentacion='");
            sb.Append(XmlEscapeAttr(ctx.IdiomasCodigoPresentacion));
            sb.Append("'><![CDATA[");
            sb.Append(ctx.ServerPresentacion);
            sb.Append("]]></train>");
        }
        else
        {
            sb.Append("<estacion id='");
            sb.Append(XmlEscapeAttr(ctx.ServerNemonico));
            sb.Append("' idPCEstacion='");
            sb.Append(ctx.ServerPcId);
            sb.Append("' idVia='");
            sb.Append(XmlEscapeAttr(idViaConcatenado));
            sb.Append("'  idiomas_presentacion='");
            sb.Append(XmlEscapeAttr(ctx.IdiomasPresentacion));
            sb.Append("' estacionTermino='");
            sb.Append(estacionTermino);
            sb.Append("'><![CDATA[");
            sb.Append(ctx.ServerPresentacion);
            sb.Append("]]></estacion>");
        }

        // Player
        sb.Append("<Player denoExterna='");
        sb.Append(XmlEscapeAttr(ctx.DenoExterna ?? string.Empty));
        sb.Append("'></Player>");

        // SE (Sistemas Externos)
        sb.Append("<SE>");
        foreach (var se in ctx.SistemasExternos)
        {
            sb.Append("<SSE id=\"");
            sb.Append(se);
            sb.Append("\"/>\r\n");
        }
        sb.Append("</SE>");

        // lineas / trayectos / stops
        sb.Append("<lineas>");
        AppendLineas(sb, ctx.Lineas);
        sb.Append("</lineas>");

        // estaciones
        if (ctx.Estaciones.Count > 0)
        {
            sb.Append("<estaciones>");
            foreach (var est in ctx.Estaciones)
            {
                sb.Append("<estacion id='");
                sb.Append(XmlEscapeAttr(est.NemoPuntoControl));
                sb.Append("' type_jorney='' recurso_externalID='");
                sb.Append(est.AudioExternalId);
                sb.Append("'>");
                sb.Append("<deno><![CDATA[");
                sb.Append(est.DenominacionCData);
                sb.Append("]]></deno>");

                if (est.Prestaciones.Count > 0)
                {
                    sb.Append("<prestaciones>");
                    foreach (var p in est.Prestaciones)
                        sb.Append("<prestacion url_servicio='").Append(XmlEscapeAttr(p.UrlServicio)).Append("' />");
                    sb.Append("</prestaciones>");
                }

                if (est.Correspondencias.Count > 0)
                {
                    sb.Append("<correspondencias>");
                    foreach (var c in est.Correspondencias)
                    {
                        sb.Append("<correspondencia url_img='");
                        sb.Append(XmlEscapeAttr(c.UrlImg));
                        sb.Append("' deno='");
                        sb.Append(XmlEscapeAttr(c.Deno));
                        sb.Append("' bgColor='0x");
                        sb.Append(XmlEscapeAttr(c.BgColorHex));
                        sb.Append("'/>");
                    }
                    sb.Append("</correspondencias>");
                }

                sb.Append("</estacion>");
            }
            sb.Append("</estaciones>");
        }

        // lineas generales
        sb.Append("<lineas_generales>");
        foreach (var l in ctx.LineasGenerales.OrderBy(x => x.OrdenVisualLineasEstados))
        {
            sb.Append("<linea id='").Append(l.idLineas)
                .Append("' deno='").Append(XmlEscapeAttr(l.Identificador))
                .Append("' id_medio_transporte='").Append(l.IdMedioTransporte)
                .Append("' bgColor='0x").Append(XmlEscapeAttr(l.ColorHex))
                .Append("' fgColor='0x").Append(XmlEscapeAttr(l.ColorLetraHex))
                .Append("' estado_default='1'")
                .Append(" modo_presentacion_paneles='").Append(l.ModoPresentacionPaneles)
                .Append("' ordenVisualLineasEstados='").Append(l.OrdenVisualLineasEstados)
                .Append("' url_img=''")
                .Append(" AudioExternalID='").Append(l.AudioExternalId)
                .Append("'></linea>");
        }
        sb.Append("</lineas_generales>");

        // lineas estados
        if (ctx.LineasEstados.Count > 0)
        {
            sb.Append("<lineas_estados tiempo_segundos_presentacion_idioma='");
            sb.Append(DefaultLineasEstadosSeconds);
            sb.Append("' >");
            foreach (var le in ctx.LineasEstados)
            {
                sb.Append("<linea_estado id='").Append(le.idlineas_estados)
                    .Append("' bgColor='#").Append(XmlEscapeAttr(le.Color))
                    .Append("' fgColor='#").Append(XmlEscapeAttr(le.ColorLetra))
                    .Append("'><title>");
                //sb.Append(XmlEscapeText(le.Descripcion.Replace("'", "`").Replace("\"", "'")));
                sb.Append(le.Descripcion.Replace("'", "`").Replace("\"", "'"));
                sb.Append("</title></linea_estado>");
            }
            sb.Append("</lineas_estados>");
        }

        // medios transporte
        sb.Append("<medios_transporte>");
        foreach (var mt in ctx.MediosTransporte)
        {
            sb.Append("<medio_transporte id='");
            sb.Append(mt.idMedios_transporte);
            sb.Append("' logo_img='' logo_aux_img=''>");
            sb.Append("<title><![CDATA[");
            sb.Append(mt.Denominacion);
            sb.Append("]]></title>");
            sb.Append("<description>");
            //sb.Append(XmlEscapeText(mt.Presentacion.Replace("'", "`").Replace("\"", "'")));
            sb.Append(mt.Presentacion.Replace("'", "`").Replace("\"", "'"));
            sb.Append("</description>");
            sb.Append("</medio_transporte>");
        }
        sb.Append("</medios_transporte>");

        // horarios
        sb.Append("<horarios>");
        foreach (var h in ctx.Horarios)
        {
            sb.Append("<horario tipo='").Append(h.idhorarios_tarifas)
                .Append("' bgColor='#").Append(XmlEscapeAttr(h.Color))
                .Append("' fgColor='#").Append(XmlEscapeAttr(h.ColorLetra))
                .Append("'>");
            //sb.Append(XmlEscapeText(h.Presentacion.Replace("'", "`").Replace("\"", "'")));
            sb.Append(h.Presentacion.Replace("'", "`").Replace("\"", "'"));
            sb.Append("</horario>");
        }
        sb.Append("</horarios>");

        // settings (ya viene en XML)
        if (!string.IsNullOrWhiteSpace(ctx.SettingsXml))
            sb.Append(ctx.SettingsXml.Replace('"', '\''));

        // presentaciones
        sb.Append("<presentaciones efecto='");
        sb.Append(XmlEscapeAttr(ctx.Efecto ?? string.Empty));
        sb.Append("'>");
        foreach (var p in ctx.Presentaciones.Items)
        {
            sb.Append("<presentacion tiempo='").Append(p.Tiempo).Append("'><![CDATA[");
            sb.Append(p.Presentacion);
            sb.Append("]]></presentacion>");
        }
        sb.Append("</presentaciones>");

        sb.Append("</datasource>");
        
        return sb.ToString();
    }

    private static string BuildIdViaConcatenado(InfoStationContext ctx)
    {
        var vias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in ctx.Lineas)
        {
            foreach (var t in l.Trayectos)
            {
                if (!string.IsNullOrWhiteSpace(t.IdVia))
                    vias.Add(t.IdVia.Trim());
            }
        }
        return string.Join(";", vias);
    }

    private static void AppendLineas(StringBuilder sb, IReadOnlyList<InfoStationLinea> lineas)
    {
        foreach (var linea in lineas)
        {
            sb.Append("<linea id='");
            sb.Append(linea.IdLinea);
            sb.Append("'>");
            foreach (var t in linea.Trayectos)
            {
                sb.Append("<trayecto id='").Append(t.IdTrayecto)
                    .Append("' direccion_id='").Append(XmlEscapeAttr(t.DireccionNemonico))
                    .Append("' parcial='").Append(t.Parcial)
                    .Append("' idVia='").Append(XmlEscapeAttr(t.IdVia))
                    .Append("'>");
                sb.Append("<deno><![CDATA[").Append(t.Denominacion ?? string.Empty).Append("]]></deno>");
                sb.Append("<stops>");
                foreach (var s in t.Stops)
                {
                    sb.Append("<stop id='").Append(XmlEscapeAttr(s.NemoPuntoControl))
                        .Append("' order='").Append(s.Orden)
                        .Append("' time_stop='").Append(s.TiempoParada)
                        .Append("' time_next_stop='").Append(s.TiempoSiguienteParada)
                        .Append("' type_jorney='").Append(XmlEscapeAttr(s.TipoJourney ?? string.Empty))
                        .Append("'></stop>");
                }
                sb.Append("</stops>");
                sb.Append("</trayecto>");
            }
            sb.Append("</linea>\r\n");
        }
    }

    private static string XmlEscapeAttr(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return SecurityElement.Escape(value) ?? string.Empty;
    }

    private static string XmlEscapeText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
