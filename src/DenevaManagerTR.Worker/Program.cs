using DenevaManagerTR.Application.InfoStation;
using DenevaManagerTR.Application.Jobs;
using DenevaManagerTR.Application.LineasEstado;
using DenevaManagerTR.Application.Options;
using DenevaManagerTR.Application.Rabbit;
using DenevaManagerTR.Application.Scheduling;
using DenevaManagerTR.Core.Ports;
using DenevaManagerTR.Infrastructure;
using DenevaManagerTR.Infrastructure.Logging;
using DenevaManagerTR.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;
using Artifact.Transit.Logging;

// Register CodePages encoding provider for legacy encoding support (1252)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add environment variables for Deneva configuration
builder.Configuration.AddEnvironmentVariables(prefix: "DENEVA_CONFIG_");

// Configure Artifact.Transit.Logging with audit enabled
builder.Services.ConfigureWebApiLogging(
    enableAuditLogging: true,
    profile: LoggingProfile.WebApi);

// Add Infrastructure services (Config, Crypto, Repositories, HttpClient)
builder.Services.AddInfrastructureServices(builder.Configuration);

// Configure application options
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection("Jobs"));

var runMode = (builder.Configuration["RunMode"] ?? "Heartbeat").Trim();

// Application services
builder.Services.AddSingleton<RoutingKeyBuilder>();
builder.Services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();
builder.Services.AddSingleton<ILineasEstadosService, LineasEstadosService>();

// =========================
// MODOS DE EJECUCIÓN
// =========================

if (runMode.Equals("InfoStation", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IInfoStationFileLogger, InfoStationFileLogger>();
    builder.Services.AddSingleton<IInfoStationService, InfoStationService>();
    builder.Services.AddHostedService<InfoStationConsumerHostedService>();
}
else if (runMode.Equals("InfoLineasEstado", StringComparison.OrdinalIgnoreCase))
{
    // 🔹 SOLO GETINFOLINEASESTADOS
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

