using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;
using System.Security.Cryptography;
using System.Text;

namespace DenevaManagerTR.Infrastructure.Config;

public sealed class DenevaConfigReader : IDenevaConfigReader
{
    private readonly ILogger<DenevaConfigReader> _logger;
    private readonly IOptionsMonitor<DenevaConfigOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly object _lock = new();
    private DateTime _lastWriteUtc;
    private string? _lastETag;
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
        lock (_lock)
        {
            var opts = _options.CurrentValue;

            // Intentar cargar desde URL si está configurada
            if (!string.IsNullOrWhiteSpace(opts.Url))
            {
                if (TryLoadFromUrl(opts))
                    return;
                
                _logger.LogWarning("No se pudo cargar deneva.config desde URL, intentando fallback a fichero local");
            }

            // Fallback a fichero local
            TryLoadFromFile(opts.Path);
        }
    }

    private bool TryLoadFromUrl(DenevaConfigOptions opts)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("DenevaConfigClient");
            
            var request = new HttpRequestMessage(HttpMethod.Get, opts.Url);

            // Obtener Authorization: primero de variable de entorno, luego de configuración
            var authorization = Environment.GetEnvironmentVariable("DENEVA_CONFIG_AUTHORIZATION") ?? opts.Authorization;
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                request.Headers.Add("Authorization", authorization);
            }

            // Añadir If-None-Match si tenemos un ETag previo para evitar descargas innecesarias
            if (!string.IsNullOrWhiteSpace(_lastETag))
            {
                request.Headers.Add("If-None-Match", _lastETag);
            }

            var response = httpClient.Send(request);

            // 304 Not Modified - el contenido no ha cambiado
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _logger.LogDebug("deneva.config no ha cambiado (304 Not Modified)");
                return _doc is not null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Error descargando deneva.config desde URL: {StatusCode}", response.StatusCode);
                return false;
            }

            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            
            // Calcular hash del contenido para detectar cambios
            var contentHash = ComputeHash(content);
            
            // Si el hash es igual al anterior, no recargamos
            if (_lastContentHash == contentHash && _doc is not null)
            {
                _logger.LogDebug("deneva.config no ha cambiado (mismo hash)");
                return true;
            }

            // Parsear el XML
            _doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            _lastContentHash = contentHash;
            
            // Guardar ETag si está presente
            if (response.Headers.TryGetValues("ETag", out var etagValues))
            {
                _lastETag = etagValues.FirstOrDefault();
            }

            _logger.LogInformation("deneva.config cargado desde URL. Url={Url}", opts.Url);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cargando deneva.config desde URL. Url={Url}", opts.Url);
            return false;
        }
    }

    private void TryLoadFromFile(string path)
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
            if (_doc is not null && writeUtc == _lastWriteUtc)
                return;

            _doc = XDocument.Load(path, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            _lastWriteUtc = writeUtc;
            _logger.LogInformation("deneva.config cargado desde fichero. Path={Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo cargar deneva.config desde fichero. Path={Path}", path);
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
