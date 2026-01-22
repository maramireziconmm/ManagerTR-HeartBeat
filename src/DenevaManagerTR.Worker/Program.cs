using DenevaManagerTR.Infrastructure;
using System.Text;

// Register CodePagesEncodingProvider for encoding 1252 support
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Create WebApplicationBuilder instead of Host.CreateDefaultBuilder
var builder = WebApplication.CreateBuilder(args);

// Configure Serilog logging from Artifact.Transit.Logging
var serilogPath = Environment.GetEnvironmentVariable("SERILOG_PATH") 
    ?? builder.Configuration["SerilogConfiguration:PathLog"] 
    ?? "/app/logs";

try
{
    // Try to use Artifact.Transit.Logging if available
    // This is a dynamic call since the package may not be available in all environments
    var loggingAssembly = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "Artifact.Transit.Logging");
    
    if (loggingAssembly != null)
    {
        var extensionsType = loggingAssembly.GetType("Artifact.Transit.Logging.WebApplicationBuilderExtensions");
        if (extensionsType != null)
        {
            var method = extensionsType.GetMethod("ConfigureWebApiLogging");
            if (method != null)
            {
                // ConfigureWebApiLogging(builder, pathLog, enableAuditLogging)
                method.Invoke(null, new object[] { builder, serilogPath, true });
                Console.WriteLine($"Artifact.Transit.Logging configured with audit logging enabled. PathLog: {serilogPath}");
            }
        }
    }
    else
    {
        // Fallback to basic Serilog configuration if Artifact.Transit.Logging is not available
        Console.WriteLine("Artifact.Transit.Logging not available, using fallback Serilog configuration");
        ConfigureFallbackLogging(builder, serilogPath);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error configuring Artifact.Transit.Logging, using fallback: {ex.Message}");
    ConfigureFallbackLogging(builder, serilogPath);
}

// Register Infrastructure services via ServiceExtensions
builder.Services.AddInfrastructureServices(builder.Configuration);

// Build the application
var app = builder.Build();

// Run the application
await app.RunAsync();

// Fallback logging configuration method
static void ConfigureFallbackLogging(WebApplicationBuilder builder, string pathLog)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information);
    
    Console.WriteLine($"Fallback logging configured. Console logging enabled. PathLog would be: {pathLog}");
}

