using DenevaManagerTR.Application.InfoStation;
using DenevaManagerTR.Application.Jobs;
using DenevaManagerTR.Application.LineasEstado;
using DenevaManagerTR.Application.Options;
using DenevaManagerTR.Application.Rabbit;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Infrastructure.Config;
using DenevaManagerTR.Infrastructure.Crypto;
using DenevaManagerTR.Infrastructure.Logging;
using DenevaManagerTR.Infrastructure.Messaging;
using DenevaManagerTR.Infrastructure.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DenevaManagerTR.Infrastructure;

public static class ServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<JobsOptions>(configuration.GetSection("Jobs"));
        services.Configure<DenevaConfigOptions>(configuration.GetSection("DenevaConfig"));

        // HttpClient for DenevaConfigReader
        services.AddHttpClient("DenevaConfigClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Infra común
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDenevaConfigReader, DenevaConfigReader>();
        services.AddSingleton<ICryptoAdapter, DenevaCryptoAdapterWrapper>();
        services.AddSingleton<RoutingKeyBuilder>();
        services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();

        // Repositorios
        services.AddSingleton<TransitMySqlRepository>();
        services.AddSingleton<ITransitRepository>(sp =>
            sp.GetRequiredService<TransitMySqlRepository>());

        services.AddSingleton<InfoStationMySqlRepository>();
        services.AddSingleton<IInfoStationRepository>(sp =>
            sp.GetRequiredService<InfoStationMySqlRepository>());
        services.AddSingleton<ILineasEstadosRepository>(sp =>
            sp.GetRequiredService<InfoStationMySqlRepository>());

        // Servicios
        services.AddSingleton<ILineasEstadosService, LineasEstadosService>();

        // File loggers según RunMode
        var runMode = (configuration["RunMode"] ?? "Heartbeat").Trim();

        if (runMode.Equals("InfoStation", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IInfoStationFileLogger, InfoStationFileLogger>();
            services.AddSingleton<IInfoStationService, InfoStationService>();
        }
        else if (runMode.Equals("InfoLineasEstado", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IInfoStationFileLogger, InfoStationFileLogger>();
        }
        else // Heartbeat (default)
        {
            services.AddSingleton<IHeartbeatFileLogger, HeartbeatFileLogger>();
            services.AddSingleton<IJob, HeartbeatJob>();
        }

        return services;
    }
}
