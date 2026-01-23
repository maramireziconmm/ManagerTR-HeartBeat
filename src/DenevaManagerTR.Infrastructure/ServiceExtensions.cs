using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Infrastructure.Config;
using DenevaManagerTR.Infrastructure.Crypto;
using DenevaManagerTR.Infrastructure.MySql;
using Microsoft.Extensions.DependencyInjection;

namespace DenevaManagerTR.Infrastructure;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // HTTP Client for DenevaConfigReader
        services.AddHttpClient<IDenevaConfigReader, DenevaConfigReader>();
        
        // Core infrastructure services
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICryptoAdapter, DenevaCryptoAdapterWrapper>();
        
        // Repositories
        services.AddSingleton<TransitMySqlRepository>();
        services.AddSingleton<ITransitRepository>(sp =>
            sp.GetRequiredService<TransitMySqlRepository>());
        
        services.AddSingleton<InfoStationMySqlRepository>();
        services.AddSingleton<IInfoStationRepository>(sp =>
            sp.GetRequiredService<InfoStationMySqlRepository>());
        services.AddSingleton<ILineasEstadosRepository>(sp =>
            sp.GetRequiredService<InfoStationMySqlRepository>());
        
        return services;
    }
}
