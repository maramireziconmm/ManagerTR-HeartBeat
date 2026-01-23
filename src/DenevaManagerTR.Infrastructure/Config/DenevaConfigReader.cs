using System.Net.Http;
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
    private XDocument? _doc;
    private string? _lastHttpHash;

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
        // Priority 1: Try HTTP download if URL is configured
        var url = Environment.GetEnvironmentVariable("DENEVA_CONFIG_URL")
                  ?? _options.CurrentValue.Url;

        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var success = TryLoadFromHttp(url);
                if (success)
                {
                    return;
                }
                _logger.LogWarning("HTTP download failed or unchanged, falling back to file");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading deneva.config from {Url}", url);
            }
        }

        // Priority 2: Fallback to local file
        var path = _options.CurrentValue.Path;
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
                _logger.LogInformation("deneva.config cargado desde archivo. Path={Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo cargar deneva.config. Path={Path}", path);
        }
    }

    private bool TryLoadFromHttp(string url)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("DenevaConfigClient");

            // Configure Authorization header
            var authHeader = Environment.GetEnvironmentVariable("DENEVA_CONFIG_AUTHORIZATION")
                            ?? _options.CurrentValue.Authorization;

            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                client.DefaultRequestHeaders.Add("Authorization", authHeader);
            }

            // Download XML content
            var response = client.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var xmlContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            
            // Calculate SHA256 hash to check if content changed
            var currentHash = ComputeSha256(xmlContent);

            lock (_lock)
            {
                // Only reload if hash changed
                if (_lastHttpHash != null && _lastHttpHash == currentHash)
                {
                    _logger.LogDebug("deneva.config unchanged (same SHA256 hash)");
                    return true;
                }

                // Parse and store new XML
                _doc = XDocument.Parse(xmlContent, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                _lastHttpHash = currentHash;
                _lastWriteUtc = DateTime.UtcNow;
                _logger.LogInformation("deneva.config descargado desde HTTP. URL={Url}, Hash={Hash}", url, currentHash.Substring(0, 8));
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al descargar deneva.config desde {Url}", url);
            return false;
        }
    }

    private static string ComputeSha256(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}
