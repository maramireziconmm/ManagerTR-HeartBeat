namespace DenevaManagerTR.Core.Ports;

public interface IInfoStationFileLogger
{
    void Info(string message);
    void Xml(string xml);
}
