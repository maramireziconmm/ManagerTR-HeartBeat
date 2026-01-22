using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DenevaManagerTR.Core.Ports;
using Microsoft.Extensions.Logging;

namespace DenevaManagerTR.Application.LineasEstado
{
    public sealed class LineasEstadosService : ILineasEstadosService
    {
        private readonly ILogger<LineasEstadosService> _logger;
        private readonly ILineasEstadosRepository _repo;

        // Si quieres ignorar empresa (como ahora tu query), no pasa nada: lo dejo por compatibilidad con el legacy.
        public LineasEstadosService(ILogger<LineasEstadosService> logger, ILineasEstadosRepository repo)
        {
            _logger = logger;
            _repo = repo;
        }

        public async Task<string> GetLineasEstadosAsync(int empresa, CancellationToken ct)
        {
            var now = DateTime.Now;

            var seconds = await _repo.GetLineasEstadosSecondsAsync(ct);
            //var lineasEstados = await _repo.GetLineasEstadosAsync(ct);
            var lineasEstados = await _repo.GetInfoLineasEstadosAsync(empresa, "C:\\deneva\\resources\\DenevaWeb\\recursos\\", ct);
            //var sb = new StringBuilder(4096);

            //// XML MINIMO pero válido (sin saltos)
            //sb.Append("<?xml version='1.0' encoding='iso-8859-1'?>");
            //sb.Append("<datasource Timestamp='").Append(now.ToString("yyyy-MM-dd HH:mm:ss")).Append("'>");

            //if (lineasEstados.Count > 0)
            //{
            //    sb.Append("<lineas_estados tiempo_segundos_presentacion_idioma='")
            //      .Append(seconds)
            //      .Append("'>");

            //    foreach (var le in lineasEstados)
            //    {
            //        // IMPORTANTE: NO metas aquí XML como texto. Esto es XML real.
            //        sb.Append("<linea_estado id='").Append(le.idlineas_estados)
            //          .Append("' bgColor='").Append(le.Color)        // Color ya viene con '#'
            //          .Append("' fgColor='").Append(le.ColorLetra)   // ColorLetra ya viene con '#'
            //          .Append("'><title>");

            //        // En legacy reemplazabas comillas:
            //        var title = (le.Descripcion ?? string.Empty).Replace("'", "`").Replace("\"", "'");
            //        sb.Append(EscapeText(title));

            //        sb.Append("</title></linea_estado>");
            //    }

            //    sb.Append("</lineas_estados>");
            //}

            //sb.Append("</datasource>");
            //return sb.ToString();
            return lineasEstados.ToString();
        }

        private static string EscapeText(string s)
        {
            // Solo escapamos caracteres problemáticos EN TEXTO, no etiquetas.
            // Ojo: si no quieres ni esto, quítalo, pero te arriesgas a romper XML.
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
