namespace DenevaManagerTR.Core.Domain.Heartbeat;

public sealed record SseEntry(
    int Id,
    int ModoSeguimiento,
    int ModoTrabajo,
    int Omitir
);
