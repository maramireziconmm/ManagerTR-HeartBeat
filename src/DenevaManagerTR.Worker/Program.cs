using DenevaManagerTR.Application.InfoStation;
using DenevaManagerTR.Application.Jobs;
using DenevaManagerTR.Application.LineasEstado;
using DenevaManagerTR.Application.Options;
using DenevaManagerTR.Application.Rabbit;
using DenevaManagerTR.Application.Scheduling;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Infrastructure;
using DenevaManagerTR.Infrastructure.Config;
using DenevaManagerTR.Infrastructure.Crypto;
using DenevaManagerTR.Infrastructure.Logging;
using DenevaManagerTR.Infrastructure.Messaging;
using DenevaManagerTR.Infrastructure.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .ConfigureServices((context, services) =>
    {
        var runMode = (context.Configuration["RunMode"] ?? "Heartbeat").Trim();

        // Options
        services.Configure<JobsOptions>(context.Configuration.GetSection("Jobs"));
        services.Configure<DenevaConfigOptions>(context.Configuration.GetSection("DenevaConfig"));

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

        // =========================
        // MODOS DE EJECUCIÓN
        // =========================

        if (runMode.Equals("InfoStation", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IInfoStationFileLogger, InfoStationFileLogger>();
            services.AddSingleton<IInfoStationService, InfoStationService>();
            services.AddHostedService<InfoStationConsumerHostedService>();

        }
        else if (runMode.Equals("InfoLineasEstado", StringComparison.OrdinalIgnoreCase))
        {
            // 🔹 SOLO GETINFOLINEASESTADOS
            services.AddSingleton<IInfoStationFileLogger, InfoStationFileLogger>();
            //services.AddSingleton<IJob, HeartbeatJob>();
            // HostedService específico (recomendado)
            services.AddHostedService<InfoLineasEstadoConsumerHostedService>();
        }
        else // Heartbeat (default)
        {
            services.AddSingleton<IHeartbeatFileLogger, HeartbeatFileLogger>();
            services.AddHostedService<MultiJobHostedService>();
            services.AddSingleton<IJob,HeartbeatJob>();
        }
    })
    .Build();

await host.RunAsync();

