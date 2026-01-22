namespace DenevaManagerTR.Core.Ports;

public interface IHeartbeatFileLogger
{
    void Info(string message);
    void Xml(string xml);
}
