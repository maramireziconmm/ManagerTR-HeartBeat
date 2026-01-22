using System.Linq;
using System.Xml.Linq;
using DenevaManagerTR.Core.Domain.Heartbeat;

namespace DenevaManagerTR.Application.Builders;

public static class HeartbeatXmlBuilder
{
    public static string Build(
        string originSystemValue,
        int modoSeguimiento,
        int modoTrabajo,
        long lastUpdateMs,
        string infoHorario = "",
        IEnumerable<SseEntry>? seEntries = null)
    {
        var hb =
            new XElement("HeartBeat",
                new XAttribute("version", "1.0"),
                new XElement("OriginSystem", new XAttribute("value", originSystemValue)),
                new XElement("modoSeguimiento", new XAttribute("value", modoSeguimiento)),
                new XElement("modoTrabajo", new XAttribute("value", modoTrabajo)),
                new XElement("LastUpdate", new XAttribute("value", lastUpdateMs.ToString())),
                new XElement("InfoHorario", new XAttribute("value", infoHorario ?? string.Empty))
            );

        if (seEntries is not null)
        {
            var list = seEntries.ToList();
            if (list.Count > 0)
            {
                var se = new XElement("SE",
                    list.Select(e => new XElement("SSE",
                        new XAttribute("id", e.Id),
                        new XAttribute("modoSeguimiento", e.ModoSeguimiento),
                        new XAttribute("modoTrabajo", e.ModoTrabajo),
                        new XAttribute("omitir", e.Omitir)
                    )));
                hb.Add(se);
            }
        }

        return hb.ToString(SaveOptions.DisableFormatting);
    }
}
