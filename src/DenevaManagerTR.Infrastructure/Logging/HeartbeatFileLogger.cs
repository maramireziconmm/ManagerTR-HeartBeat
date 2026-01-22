using System.Text;
using DenevaManagerTR.Core.Ports;

namespace DenevaManagerTR.Infrastructure.Logging;

public sealed class HeartbeatFileLogger : IHeartbeatFileLogger
{
    private static readonly object _lock = new();

    private readonly string _baseDir;

    public HeartbeatFileLogger()
    {
        _baseDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_baseDir);
    }

    public void Info(string message)
    {
        WriteLine(GetGenericPath(), message);
    }

    public void Xml(string xml)
    {
        WriteLine(GetXmlPath(), xml);
    }

    private string GetGenericPath()
        => Path.Combine(_baseDir, $"DenevaManagerTR.Heartbeat-{DateTime.Now:yyyyMMdd}.log");

    private string GetXmlPath()
        => Path.Combine(_baseDir, $"DenevaManagerTR.Heartbeat.RabbitMQ-{DateTime.Now:yyyyMMdd}.log");

    private void WriteLine(string file, string text)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {text}";
        lock (_lock)
        {
            File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
