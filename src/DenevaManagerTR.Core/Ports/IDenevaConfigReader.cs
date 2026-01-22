namespace DenevaManagerTR.Core.Ports;

public interface IDenevaConfigReader
{
    string? Get(string section, string key);
}
