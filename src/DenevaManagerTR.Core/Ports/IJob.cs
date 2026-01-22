namespace DenevaManagerTR.Core.Ports;

public interface IJob
{
    string Name { get; }
    Task ExecuteAsync(CancellationToken cancellationToken);
}
