using System.Text;
using DenevaManagerTR.Application.InfoStation;
using DenevaManagerTR.Application.Jobs;
using DenevaManagerTR.Application.LineasEstado;
using DenevaManagerTR.Application.Options;
using DenevaManagerTR.Application.Rabbit;
using DenevaManagerTR.Application.Scheduling;
using DenevaManagerTR.Core.Options;
using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Infrastructure;
using DenevaManagerTR.Infrastructure.Logging;
using DenevaManagerTR.Infrastructure.Messaging;
using DenevaManagerTR.Infrastructure.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Register encoding provider for .NET compatibility
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// TODO: Uncomment when Artifact.Transit.Logging is available from private feed
// Configure Artifact.Transit.Logging with audit logging enabled
// builder.Services.AddTransitLogging(new TransitLoggingOptions
// {
//     EnableAuditLogging = true
// });

// Configure options from configuration
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection("Jobs"));
builder.Services.Configure<DenevaConfigOptions>(builder.Configuration.GetSection("DenevaConfig"));

// Add infrastructure services (IClock, IDenevaConfigReader, ICryptoAdapter, HttpClient)
builder.Services.AddInfrastructureServices();

// Messaging services
builder.Services.AddSingleton<RoutingKeyBuilder>();
builder.Services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();

// Repositories
builder.Services.AddSingleton<TransitMySqlRepository>();
builder.Services.AddSingleton<ITransitRepository>(sp =>
    sp.GetRequiredService<TransitMySqlRepository>());

builder.Services.AddSingleton<InfoStationMySqlRepository>();
builder.Services.AddSingleton<IInfoStationRepository>(sp =>
    sp.GetRequiredService<InfoStationMySqlRepository>());
builder.Services.AddSingleton<ILineasEstadosRepository>(sp =>
    sp.GetRequiredService<InfoStationMySqlRepository>());

// Application services
builder.Services.AddSingleton<ILineasEstadosService, LineasEstadosService>();

// =========================
// RUN MODE CONFIGURATION
// =========================
var runMode = (builder.Configuration["RunMode"] ?? "Heartbeat").Trim();

if (runMode.Equals("InfoStation", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IInfoStationFileLogger, InfoStationFileLogger>();
    builder.Services.AddSingleton<IInfoStationService, InfoStationService>();
    builder.Services.AddHostedService<InfoStationConsumerHostedService>();
}
else if (runMode.Equals("InfoLineasEstado", StringComparison.OrdinalIgnoreCase))
{
    // SOLO GETINFOLINEASESTADOS
    builder.Services.AddSingleton<IInfoStationFileLogger, InfoStationFileLogger>();
    builder.Services.AddHostedService<InfoLineasEstadoConsumerHostedService>();
}
else // Heartbeat (default)
{
    builder.Services.AddSingleton<IHeartbeatFileLogger, HeartbeatFileLogger>();
    builder.Services.AddHostedService<MultiJobHostedService>();
    builder.Services.AddSingleton<IJob, HeartbeatJob>();
}

var app = builder.Build();

await app.RunAsync();
