using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;

namespace DenevaManagerTR.Infrastructure.Config;

public sealed class DenevaConfigReader : IDenevaConfigReader
{
    private readonly ILogger<DenevaConfigReader> _logger;
    private readonly IOptionsMonitor<DenevaConfigOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly object _lock = new();
    private DateTime _lastWriteUtc;
    private string? _lastContentHash;
    private XDocument? _doc;

    public DenevaConfigReader(
        ILogger<DenevaConfigReader> logger, 
        IOptionsMonitor<DenevaConfigOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    public string? Get(string section, string key)
    {
        EnsureLoaded();
        if (_doc?.Root is null) return null;

        try
        {
            var root = _doc.Root;

            // IMPORTANTE:
            // En deneva.config suele existir también la definición de sección en <configSections>:
            //   <section name="RabbiMQ.My.MySettings" ... />
            // y el bloque real de valores en <applicationSettings>:
            //   <RabbiMQ.My.MySettings> ... </RabbiMQ.My.MySettings>
            //
            // Si buscamos primero por atributo name=..., podemos caer en <section name="..."> (definición),
            // que NO contiene los <setting ...>. Por eso:
            // 1) priorizamos el elemento cuyo *nombre* sea exactamente la sección (bloque real)
            // 2) si no existe, entonces probamos por atributo name=... excluyendo <section>.

            XElement? sectionNode =
                root.Descendants().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, section, StringComparison.OrdinalIgnoreCase))
                ??
                root.Descendants().FirstOrDefault(e =>
                    !string.Equals(e.Name.LocalName, "section", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)e.Attribute("name"), section, StringComparison.OrdinalIgnoreCase));

            // Si no existe la sección, buscamos globalmente (comportamiento tolerante)
            var candidates = sectionNode is null ? root.Descendants() : sectionNode.Descendants();

            // Soporta ambos formatos habituales:
            //  a) <add key="X" value="Y" />
            //  b) <setting name="X"><value>Y</value></setting>
            var setting = candidates.FirstOrDefault(e =>
                string.Equals((string?)e.Attribute("key"), key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)e.Attribute("name"), key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)e.Attribute("id"), key, StringComparison.OrdinalIgnoreCase));

            if (setting is null) return null;

            // Formato a) attribute value=""
            var attrValue = (string?)setting.Attribute("value");
            if (!string.IsNullOrWhiteSpace(attrValue)) return attrValue.Trim();

            // Formato b) <value>...</value>
            var valueChild = setting.Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, "value", StringComparison.OrdinalIgnoreCase));

            if (valueChild is not null)
            {
                var v = (valueChild.Value ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }

            // Fallback: texto interno del nodo (por si hubiera otro esquema)
            var inner = (setting.Value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(inner) ? null : inner;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo deneva.config. Section={Section} Key={Key}", section, key);
            return null;
        }
    }

    private void EnsureLoaded()
    {
        var opts = _options.CurrentValue;
        
        // Prioridad 1: Intentar descargar desde URL (o variable de entorno DENEVA_CONFIG_URL)
        var url = Environment.GetEnvironmentVariable("DENEVA_CONFIG_URL") ?? opts.Url;
        
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (TryLoadFromHttp(url, opts))
                return;
            
            _logger.LogWarning("Falló descarga HTTP desde {Url}, intentando fallback a archivo local", url);
        }
        
        // Prioridad 2: Fallback a archivo local
        LoadFromFile(opts.Path);
    }

    private bool TryLoadFromHttp(string url, DenevaConfigOptions opts)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("DenevaConfigClient");
            
            // Encabezado Authorization: priorizar variable de entorno sobre configuración
            var authorization = Environment.GetEnvironmentVariable("DENEVA_CONFIG_AUTHORIZATION") ?? opts.Authorization;
            
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                request.Headers.Add("Authorization", authorization);
            }

            var response = httpClient.Send(request);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP GET falló. StatusCode={StatusCode} Url={Url}", 
                    response.StatusCode, url);
                return false;
            }

            using var stream = response.Content.ReadAsStream();
            using var reader = new StreamReader(stream);
            var xmlContent = reader.ReadToEnd();
            
            // Calcular SHA256 del contenido
            var contentHash = ComputeSha256(xmlContent);
            
            lock (_lock)
            {
                // Solo recargar si el hash cambió
                if (_doc is not null && contentHash == _lastContentHash)
                {
                    _logger.LogDebug("deneva.config sin cambios (mismo hash SHA256)");
                    return true;
                }

                _doc = XDocument.Parse(xmlContent, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                _lastContentHash = contentHash;
                _logger.LogInformation("deneva.config descargado desde HTTP. Url={Url} Hash={Hash}", url, contentHash);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error descargando deneva.config desde HTTP. Url={Url}", url);
            return false;
        }
    }

    private void LoadFromFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
            {
                _logger.LogWarning("No existe el fichero deneva.config: {Path}", path);
                return;
            }

            var writeUtc = fi.LastWriteTimeUtc;
            lock (_lock)
            {
                if (_doc is not null && writeUtc == _lastWriteUtc)
                    return;

                _doc = XDocument.Load(path, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                _lastWriteUtc = writeUtc;
                _lastContentHash = null; // Clear HTTP hash when loading from file
                _logger.LogInformation("deneva.config cargado desde archivo local. Path={Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo cargar deneva.config desde archivo. Path={Path}", path);
        }
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
