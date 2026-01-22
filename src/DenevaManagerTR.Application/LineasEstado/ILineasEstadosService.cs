using System.Threading;
using System.Threading.Tasks;

namespace DenevaManagerTR.Application.LineasEstado
{
    public interface ILineasEstadosService
    {
        Task<string> GetLineasEstadosAsync(int empresa, CancellationToken ct);
    }
}
