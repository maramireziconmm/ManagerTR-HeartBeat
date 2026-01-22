namespace DenevaManagerTR.Core.Ports;

public interface ITransitRepository
{
    /// <summary>
    /// Devuelve el LastUpdate en milisegundos epoch (Unix) para una tabla de transact_tables.
    /// </summary>
    Task<long> GetLastUpdateMsAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la lista de SSE (servidores_sistemas_externos) que deben incluirse en el HeartBeat (&lt;SE&gt;...&lt;/SE&gt;).
    /// Regla:
    /// - Si existen sistemas externos de seguimiento habilitados (SE_Enabled=1 e IsSeguimiento=1), se filtra por esos IdSistemaExterno.
    /// - Si no existen, se devuelven los SSE con Enabled=1.
    /// </summary>
    Task<IReadOnlyList<DenevaManagerTR.Core.Domain.Heartbeat.SseEntry>> GetHeartbeatSeEntriesAsync(CancellationToken cancellationToken = default);
}
