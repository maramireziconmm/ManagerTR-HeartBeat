using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DenevaManagerTR.Core.Ports;

public interface ILineasEstadosRepository
{
    public Task<int> GetLineasEstadosSecondsAsync(CancellationToken ct);
    public Task<IReadOnlyList<LineaEstado>> GetLineasEstadosAsync(CancellationToken ct);

    public Task<string> GetInfoLineasEstadosAsync(int empresa, string DirectorioRecursos, CancellationToken ct);
    
}

