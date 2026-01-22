# DenevaManagerTR – v1.0.0 (.NET 9 Migration)

## Overview

DenevaManagerTR is a .NET 9 worker service that manages heartbeat, InfoStation, and InfoLineasEstado operations with RabbitMQ integration. This version has been migrated to use WebApplicationBuilder, HTTP-based configuration loading, and integrated audit logging via Artifact.Transit.Logging.

## Version History

### v1.0.0 – .NET 9 Migration with WebApplicationBuilder and HTTP Config
- **Migrated to .NET 9**: All projects now target `net9.0`
- **WebApplicationBuilder**: Replaced `Host.CreateDefaultBuilder` with `WebApplication.CreateBuilder`
- **HTTP Config Loading**: `deneva.config` can now be downloaded from a URL with Authorization header support
- **Artifact.Transit.Logging**: Integrated with audit logging enabled by default
- **ServiceExtensions**: Dependency injection reorganized into `ServiceExtensions.cs` for better maintainability
- **Docker Support**: Updated to .NET 9 base images with docker-compose example

### v0.3.9 – InfoStation (consumidor + respuesta)
Esta entrega compila por defecto **solo** la funcionalidad de InfoStation.

#### Selección de funcionalidad (compile-time)
En `Directory.Build.props`:
- `DENEVA_INFOSTATION` -> compila y registra el consumidor de RabbitMQ para `GETINFOSTATION` y publica `SYNCINFOSTATION_RETURN`.
- `DENEVA_HEARTBEAT` -> compila y registra el job de HeartBeat.

Para cambiarlo, sustituye el símbolo en `DefineConstants`.

### v0.3.3
- HeartBeat: Header OriginSystem/ContentType y routingKey desde appsettings (Jobs:Heartbeat).
- Logging a fichero: logs/DenevaManagerTR.Heartbeat-YYYYMMDD.log y logs/DenevaManagerTR.Heartbeat.RabbitMQ-YYYYMMDD.log.

### v0.2.10 – Soporte Encoding 1252 en .NET 8
Se registra `CodePagesEncodingProvider` al inicio (Program.cs) y se añade el paquete `System.Text.Encoding.CodePages`.
Esto evita `System.NotSupportedException: No data is available for encoding 1252`.

### v0.2.6 (Section pick fix)
El `deneva.config` incluye tanto:
- Definición de sección en `<configSections>`: `<section name="RabbiMQ.My.MySettings" .../>`
- Bloque real de valores en `<applicationSettings>`: `<RabbiMQ.My.MySettings> ... <setting name="RabbitMQ_ConnectionString"><value>...</value>`

En v0.2.5 el lector podía quedarse con el nodo `<section name="...">`, lo que hacía que no aparecieran los `<setting>` y devolviese null.

En v0.2.6 se prioriza el elemento cuyo nombre sea exactamente la sección (bloque real) y se evita usar el nodo `<section>`.

## Configuration

### Environment Variables

#### Required
- **DENEVA_CONFIG_URL**: URL to download `deneva.config` (e.g., `https://config-server.example.com/deneva.config`)
- **DENEVA_CONFIG_AUTHORIZATION**: Authorization header value for downloading config (e.g., `Bearer token123` or `Basic base64credentials`)
  - If not set, the system will attempt to read from `DenevaConfig:Authorization` in appsettings.json
- **SERILOG_PATH**: Path for Serilog log files (default: `/app/logs`)

#### Optional
- **RunMode**: Execution mode - `Heartbeat`, `InfoStation`, or `InfoLineasEstado` (default: `Heartbeat`)
- **DENEVA_CONFIG_PATH**: Fallback local path to `deneva.config` if URL download fails (default: `C:\Deneva\Resources\Config\deneva.config`)

### appsettings.json Configuration

```json
{
  "RunMode": "InfoLineasEstado",
  "DenevaConfig": {
    "Path": "C:\\Deneva\\Resources\\Config\\deneva.config",
    "Url": null,
    "Authorization": null,
    "RabbitSection": "RabbitMQ.My.MySettings"
  },
  "SerilogConfiguration": {
    "PathLog": "/app/logs"
  },
  "Database": {
    "Engine": "mysql57"
  },
  "Jobs": {
    "Heartbeat": {
      "Enabled": true,
      "IntervalSeconds": 30
    },
    "InfoStation": {
      "Enabled": true,
      "AppId": "DenevaManagerTR"
    },
    "InfoLineasEstados": {
      "Enabled": true,
      "AppId": "DenevaManagerTR"
    }
  }
}
```

### deneva.config Structure

The `deneva.config` file is an XML configuration file that contains RabbitMQ connection settings and other configuration values:

```xml
<configuration>
  <configSections>
    <section name="RabbitMQ.My.MySettings" .../>
  </configSections>
  <applicationSettings>
    <RabbitMQ.My.MySettings>
      <setting name="RabbitMQ_ConnectionString">
        <value>amqp://...</value>
      </setting>
      <!-- Other settings -->
    </RabbitMQ.My.MySettings>
  </applicationSettings>
</configuration>
```

## Deployment

### Docker Compose (Recommended)

1. Create a `.env` file with your configuration:

```bash
DENEVA_CONFIG_URL=https://config-server.example.com/deneva.config
DENEVA_CONFIG_AUTHORIZATION=Bearer your-token-here
SERILOG_PATH=/app/logs
```

2. Run the service:

```bash
docker-compose up -d
```

3. View logs:

```bash
docker-compose logs -f denevamanagertr-worker
```

### Docker Build and Run

```bash
# Build the image
docker build -t denevamanagertr:latest .

# Run the container
docker run -d \
  -e DENEVA_CONFIG_URL=https://config-server.example.com/deneva.config \
  -e DENEVA_CONFIG_AUTHORIZATION="Bearer your-token-here" \
  -e SERILOG_PATH=/app/logs \
  -v $(pwd)/logs:/app/logs \
  --name denevamanagertr-worker \
  denevamanagertr:latest
```

### Local Development

1. Install .NET 9 SDK
2. Configure `appsettings.json` or set environment variables
3. Build the solution:

```bash
dotnet restore
dotnet build
```

4. Run the worker:

```bash
cd src/DenevaManagerTR.Worker
dotnet run
```

## NuGet Package Feed Configuration

### Artifact.Transit.Logging Private Feed

The project uses `Artifact.Transit.Logging` which may be hosted on a private NuGet feed. To restore packages:

1. Create a `nuget.config` in the solution root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="private-feed" value="https://your-private-feed.com/v3/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <private-feed>
      <add key="Username" value="%NUGET_USERNAME%" />
      <add key="ClearTextPassword" value="%NUGET_PASSWORD%" />
    </private-feed>
  </packageSourceCredentials>
</configuration>
```

2. Set credentials as environment variables:

```bash
export NUGET_USERNAME=your-username
export NUGET_PASSWORD=your-password
```

### CI/CD Configuration

For CI/CD pipelines (GitHub Actions, Azure DevOps, etc.), store credentials as secrets and configure them before restore:

```yaml
# Example for GitHub Actions
- name: Setup NuGet
  run: |
    dotnet nuget add source "https://your-private-feed.com/v3/index.json" \
      --name "private-feed" \
      --username "${{ secrets.NUGET_USERNAME }}" \
      --password "${{ secrets.NUGET_PASSWORD }}" \
      --store-password-in-clear-text
```

## Run Modes

The application supports three execution modes configured via `RunMode` environment variable or appsettings:

1. **Heartbeat** (default): Periodic heartbeat messages to RabbitMQ
2. **InfoStation**: Consumer for `GETINFOSTATION` messages with `SYNCINFOSTATION_RETURN` responses
3. **InfoLineasEstado**: Consumer for `GETINFOLINEASESTADOS` messages with `SYNCINFOLINEASESTADOSTM_RETURN` responses

## Logging

### Serilog with Audit Logging

Logs are written to:
- Console (always enabled)
- File system at `SERILOG_PATH` (default: `/app/logs`)
- Audit logs are enabled by default via Artifact.Transit.Logging

Log file naming conventions:
- `DenevaManagerTR.Heartbeat-YYYYMMDD.log`
- `DenevaManagerTR.Heartbeat.RabbitMQ-YYYYMMDD.log`

### Fallback Logging

If Artifact.Transit.Logging is not available (e.g., package not restored), the application falls back to basic console logging.

## Architecture

### Project Structure

```
DenevaManagerTR/
├── src/
│   ├── DenevaManagerTR.Core/          # Domain models and interfaces
│   ├── DenevaManagerTR.Application/   # Application services and use cases
│   ├── DenevaManagerTR.Infrastructure/# Infrastructure implementations
│   │   ├── Config/
│   │   │   └── DenevaConfigReader.cs  # HTTP + file-based config reader
│   │   ├── ServiceExtensions.cs       # DI registration
│   │   └── ...
│   └── DenevaManagerTR.Worker/        # Entry point with WebApplicationBuilder
│       ├── Program.cs
│       └── appsettings.json
├── Dockerfile                          # .NET 9 Docker image
├── docker-compose.yml                  # Docker Compose configuration
└── README.md
```

### Key Features

- **HTTP Config Loading**: Downloads `deneva.config` from a URL with Authorization header
- **Fallback to Local File**: If URL download fails, falls back to local file path
- **Caching with ETag**: Avoids unnecessary downloads using ETag and content hashing
- **Audit Logging**: Enabled by default via Artifact.Transit.Logging
- **Clean Architecture**: Separated concerns with Core, Application, Infrastructure, and Worker projects
- **Dependency Injection**: Organized via ServiceExtensions for maintainability

## Troubleshooting

### Build Errors

**Issue**: `Unable to find package Artifact.Transit.Logging`

**Solution**: Configure private NuGet feed with credentials (see NuGet Package Feed Configuration section)

### Runtime Errors

**Issue**: `No data is available for encoding 1252`

**Solution**: The application registers `CodePagesEncodingProvider` automatically. Ensure `System.Text.Encoding.CodePages` package is restored.

**Issue**: `deneva.config not found`

**Solution**: 
1. Verify `DENEVA_CONFIG_URL` is set correctly
2. Verify `DENEVA_CONFIG_AUTHORIZATION` has valid credentials
3. Check logs for HTTP errors (401, 404, etc.)
4. Ensure fallback path `DenevaConfig:Path` points to a valid local file

### Docker Issues

**Issue**: Container starts but exits immediately

**Solution**: Check logs with `docker logs <container-id>` for configuration errors

## Contributing

1. Ensure all tests pass before committing
2. Follow existing code style and patterns
3. Update documentation for new features
4. Use meaningful commit messages

## License

(Add your license information here)

## Support

For issues or questions, please contact the development team or open an issue in the repository.

