namespace DenevaManagerTR.Core.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
