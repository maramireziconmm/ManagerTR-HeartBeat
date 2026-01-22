using System.Globalization;
using System.Text;
using DenevaManagerTR.Core.Ports;

namespace DenevaManagerTR.Infrastructure.Logging;

public sealed class InfoStationFileLogger : IInfoStationFileLogger
{
    private static readonly object _lock = new();
    private readonly string _logDir;

    public InfoStationFileLogger()
    {
        _logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);
    }

    public void Info(string message)
        => WriteLine(GetInfoPath(), message);

    public void Xml(string xml)
        => WriteLine(GetXmlPath(), xml);

    private string GetInfoPath()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Path.Combine(_logDir, $"DenevaManagerTR.InfoStation-{stamp}.log");
    }

    private string GetXmlPath()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Path.Combine(_logDir, $"DenevaManagerTR.InfoStation.RabbitMQ-{stamp}.log");
    }

    private static void WriteLine(string path, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

        lock (_lock)
        {
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
