#if !EXCLUDE_ARTIFACT_LOGGING
using Artifact.Transit.Logging;
#endif
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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Text;

// Register encoding provider for legacy Windows-1252 encoding
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

#if !EXCLUDE_ARTIFACT_LOGGING
// Configure Artifact.Transit.Logging with audit logging enabled
builder.Services.AddTransitLogging(options =>
{
    options.EnableAuditLogging = true;
});
#endif

// Configure options
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection("Jobs"));
builder.Services.Configure<DenevaConfigOptions>(builder.Configuration.GetSection("DenevaConfig"));

// Add infrastructure services (includes HttpClient, Clock, Crypto, Repositories, etc.)
builder.Services.AddInfrastructureServices();

// Add application services
builder.Services.AddSingleton<RoutingKeyBuilder>();
builder.Services.AddSingleton<IMessagePublisher, RabbitMqMessagePublisher>();
builder.Services.AddSingleton<ILineasEstadosService, LineasEstadosService>();

// Determine run mode
var runMode = (builder.Configuration["RunMode"] ?? "Heartbeat").Trim();

if (runMode.Equals("InfoStation", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IInfoStationFileLogger, InfoStationFileLogger>();
    builder.Services.AddSingleton<IInfoStationService, InfoStationService>();
    builder.Services.AddHostedService<InfoStationConsumerHostedService>();
}
else if (runMode.Equals("InfoLineasEstado", StringComparison.OrdinalIgnoreCase))
{
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

app.Run();
