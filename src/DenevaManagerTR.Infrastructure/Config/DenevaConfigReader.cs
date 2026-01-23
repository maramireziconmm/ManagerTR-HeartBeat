using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;

namespace DenevaManagerTR.Infrastructure.Config;

public sealed class DenevaConfigReader : IDenevaConfigReader
{
    private readonly ILogger<DenevaConfigReader> _logger;
    private readonly IOptionsMonitor<DenevaConfigOptions> _options;
    private readonly IHttpClientFactory? _httpClientFactory;

    private readonly object _lock = new();
    private DateTime _lastWriteUtc;
    private string? _lastHash;
    private XDocument? _doc;

    public DenevaConfigReader(
        ILogger<DenevaConfigReader> logger, 
        IOptionsMonitor<DenevaConfigOptions> options,
        IHttpClientFactory? httpClientFactory = null)
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
        
        // Try HTTP first if URL is configured
        if (!string.IsNullOrWhiteSpace(opts.Url))
        {
            TryLoadFromHttp(opts);
        }
        
        // Fallback to local file
        if (_doc is null)
        {
            TryLoadFromFile(opts.Path);
        }
    }

    private void TryLoadFromHttp(DenevaConfigOptions opts)
    {
        if (_httpClientFactory is null)
        {
            _logger.LogWarning("HttpClientFactory not available, cannot fetch config from URL");
            return;
        }

        try
        {
            var url = opts.Url ?? throw new InvalidOperationException("URL is null");
            var client = _httpClientFactory.CreateClient();
            
            // Get Authorization header from config or environment variable
            var authorization = opts.Authorization 
                ?? Environment.GetEnvironmentVariable("DENEVA_CONFIG_AUTHORIZATION");
            
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                client.DefaultRequestHeaders.Add("Authorization", authorization);
            }

            var response = client.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            
            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var hash = ComputeHash(content);
            
            lock (_lock)
            {
                // Only reload if hash changed
                if (_lastHash == hash && _doc is not null)
                {
                    return;
                }
                
                _doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                _lastHash = hash;
                _logger.LogInformation("deneva.config loaded from HTTP. URL={Url} Hash={Hash}", url, hash);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load deneva.config from HTTP. URL={Url}", opts.Url);
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
            lock (_lock)
            {
                if (_doc is not null && writeUtc == _lastWriteUtc)
                    return;

                _doc = XDocument.Load(path, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                _lastWriteUtc = writeUtc;
                _lastHash = null; // Clear HTTP hash when loading from file
                _logger.LogInformation("deneva.config cargado. Path={Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo cargar deneva.config. Path={Path}", path);
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
