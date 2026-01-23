using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Infrastructure.Config;
using DenevaManagerTR.Infrastructure.Crypto;
using Microsoft.Extensions.DependencyInjection;

namespace DenevaManagerTR.Infrastructure;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Core infrastructure services
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDenevaConfigReader, DenevaConfigReader>();
        services.AddSingleton<ICryptoAdapter, DenevaCryptoAdapterWrapper>();

        // HTTP client for DenevaConfigReader
        services.AddHttpClient<DenevaConfigReader>();

        return services;
    }
}
