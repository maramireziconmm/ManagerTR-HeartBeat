namespace DenevaManagerTR.Core.Ports;

public interface IInfoStationService
{
    /// <summary>
    /// Construye el XML de respuesta para una petición GETINFOSTATION.
    /// externalId corresponde al campo "peticion" recibido en el envelope legacy.
    /// </summary>
    Task<string> GetInformacionEstacionAsync(string externalId, CancellationToken cancellationToken);
}
