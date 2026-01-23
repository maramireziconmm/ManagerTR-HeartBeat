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
    private readonly HttpClient _httpClient;

    private readonly object _lock = new();
    private DateTime _lastWriteUtc;
    private string? _lastContentHash;
    private XDocument? _doc;

    public DenevaConfigReader(
        ILogger<DenevaConfigReader> logger, 
        IOptionsMonitor<DenevaConfigOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options;
        _httpClient = httpClient;
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
        var options = _options.CurrentValue;
        
        // Try HTTP first if URL is configured
        if (!string.IsNullOrWhiteSpace(options.Url))
        {
            if (TryLoadFromHttp(options))
                return;
                
            _logger.LogWarning("Failed to load deneva.config from HTTP, falling back to local file");
        }

        // Fallback to local file
        TryLoadFromFile(options.Path);
    }

    private bool TryLoadFromHttp(DenevaConfigOptions options)
    {
        try
        {
            // Get authorization from config or environment variable
            var authorization = options.Authorization 
                ?? Environment.GetEnvironmentVariable("DENEVA_CONFIG_AUTHORIZATION");

            if (string.IsNullOrWhiteSpace(authorization))
            {
                _logger.LogWarning("No authorization header configured for deneva.config HTTP request");
                return false;
            }

            // Create request with authorization header
            using var request = new HttpRequestMessage(HttpMethod.Get, options.Url);
            request.Headers.Add("Authorization", authorization);

            // Fetch content
            var response = _httpClient.Send(request);
            response.EnsureSuccessStatusCode();

            using var stream = response.Content.ReadAsStream();
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            // Calculate hash
            var hash = ComputeHash(content);

            lock (_lock)
            {
                // Skip reload if content hasn't changed
                if (_doc is not null && hash == _lastContentHash)
                {
                    _logger.LogDebug("deneva.config content unchanged (hash match)");
                    return true;
                }

                // Parse and cache
                _doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                _lastContentHash = hash;
                _logger.LogInformation("deneva.config loaded from HTTP. URL={Url}, Hash={Hash}", options.Url, hash);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load deneva.config from HTTP. URL={Url}", options.Url);
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
            lock (_lock)
            {
                if (_doc is not null && writeUtc == _lastWriteUtc)
                    return;

                _doc = XDocument.Load(path, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                _lastWriteUtc = writeUtc;
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
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }
}
