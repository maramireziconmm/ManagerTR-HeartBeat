# DenevaManagerTR – v0.4.0 (Migration to .NET 9 + WebApplicationBuilder)

## Overview

DenevaManagerTR is a worker service that integrates with Deneva systems for:
- **Heartbeat**: Periodic health check messages sent via RabbitMQ
- **InfoStation**: Consumer and responder for station information requests
- **InfoLineasEstado**: Consumer and responder for line status requests

## What's New in v0.4.0

### .NET 9 Migration
- Upgraded from .NET 8 to .NET 9
- Migrated from `Host.CreateDefaultBuilder` to `WebApplication.CreateBuilder`
- Updated all package references to support .NET 9

### Artifact.Transit.Logging Integration
- Integrated `Artifact.Transit.Logging` for standardized logging across services
- Enabled audit logging by default (`enableAuditLogging=true`)
- Configured Serilog.AspNetCore for structured logging

### HTTP-based Deneva Config
- **DenevaConfigReader** now supports fetching `deneva.config` via HTTP
- Configuration priority: HTTP → Local file fallback
- Hash-based caching to avoid unnecessary reloads
- Authorization header support via environment variable or configuration

### Service Extensions
- Added `ServiceExtensions.cs` to centralize dependency injection registrations
- Cleaner Program.cs with infrastructure services separated

## Configuration

### Environment Variables

#### Deneva Config - HTTP Mode (Recommended)
```bash
# URL to fetch deneva.config from
DENEVA_CONFIG_URL=https://your-config-server.com/deneva.config

# Authorization header (Bearer token or custom)
DENEVA_CONFIG_AUTHORIZATION=Bearer your-token-here
```

#### Deneva Config - Local File Mode (Fallback)
```bash
DenevaConfig__Path=/path/to/deneva.config
DenevaConfig__RabbitSection=RabbitMQ.My.MySettings
```

#### Run Mode
```bash
# Options: Heartbeat, InfoStation, InfoLineasEstado
RunMode=InfoLineasEstado
```

#### Database
```bash
Database__Engine=mysql57  # or mysql8
```

#### Jobs Configuration
See `appsettings.json` or `docker-compose.yml` for detailed job configuration options.

### Configuration Priority
1. Environment variables
2. appsettings.json
3. Default values in code

### Deneva Config Options
The `DenevaConfigOptions` class supports:
- **Url**: HTTP endpoint to fetch deneva.config (optional)
- **Authorization**: Authorization header value (optional, can use env var `DENEVA_CONFIG_AUTHORIZATION`)
- **Path**: Local file path (fallback, default: `deneva.config`)
- **RabbitSection**: Section name in deneva.config for RabbitMQ settings (default: `RabbitMQ.My.MySettings`)

## Deployment

### Docker

Build and run using Docker Compose:
```bash
docker-compose up -d
```

### Environment Variables for CI/CD

When deploying via CI/CD, ensure these variables are set:
- `DENEVA_CONFIG_URL` - URL to fetch deneva.config
- `DENEVA_CONFIG_AUTHORIZATION` - Authorization header value
- `RunMode` - Operational mode (Heartbeat, InfoStation, InfoLineasEstado)
- Database and Jobs configuration as needed

### Private NuGet Feeds

**IMPORTANT**: The package `Artifact.Transit.Logging` may be hosted in a private NuGet feed.

For CI/CD pipelines:
1. Create a `nuget.config` file with credentials:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <packageSources>
       <add key="PrivateFeed" value="https://your-private-feed.com/nuget/v3/index.json" />
     </packageSources>
     <packageSourceCredentials>
       <PrivateFeed>
         <add key="Username" value="%NUGET_USERNAME%" />
         <add key="ClearTextPassword" value="%NUGET_PASSWORD%" />
       </PrivateFeed>
     </packageSourceCredentials>
   </configuration>
   ```

2. Set secrets in your CI/CD system:
   - `NUGET_USERNAME`
   - `NUGET_PASSWORD`

### Dockerfile

The included `Dockerfile` uses .NET 9 SDK and ASP.NET 9 runtime images. Ensure your deployment environment supports these versions.

## Development

### Prerequisites
- .NET 9 SDK
- Access to private NuGet feed (if applicable)

### Build
```bash
dotnet restore
dotnet build
```

### Run
```bash
cd src/DenevaManagerTR.Worker
dotnet run
```

## Version History

### v0.4.0 – .NET 9 + WebApplicationBuilder + Artifact.Logging
- Migrated to .NET 9
- Migrated to WebApplication.CreateBuilder pattern
- Integrated Artifact.Transit.Logging with audit logging
- Added HTTP-based deneva.config fetching with hash caching
- Added ServiceExtensions for centralized DI
- Added Docker Compose example
- Updated Dockerfile to use .NET 9 images

### v0.3.9 – InfoStation (consumidor + respuesta)

Esta entrega compila por defecto **solo** la funcionalidad de InfoStation.

#### Selección de funcionalidad (compile-time)

En `Directory.Build.props`:

- `DENEVA_INFOSTATION` -> compila y registra el consumidor de RabbitMQ para `GETINFOSTATION` y publica `SYNCINFOSTATION_RETURN`.
- `DENEVA_HEARTBEAT` -> compila y registra el job de HeartBeat.

Para cambiarlo, sustituye el símbolo en `DefineConstants`.

#### Configuración

- Exchange de entrada/salida InfoStation: `RabbitMQ_InfoEstacionExchangeName` (leído desde `deneva.config`).
- Conexión RabbitMQ: `RabbitMQ_ConnectionString`, `RabbitMQ_Port`, `RabbitMQ_User`, `RabbitMQ_Pass`, `RabbitMQ_Host`, `RabbitMQ_VHost` (según tu `deneva.config`).
- Parámetros del consumidor: `Jobs:InfoStation` en `appsettings.json` (cola, topics y AppId).

### v0.3.3
- HeartBeat: Header OriginSystem/ContentType y routingKey desde appsettings (Jobs:Heartbeat).
- Logging a fichero: logs/DenevaManagerTR.Heartbeat-YYYYMMDD.log y logs/DenevaManagerTR.Heartbeat.RabbitMQ-YYYYMMDD.log.

### v0.2.10 – Soporte Encoding 1252 en .NET 8

Se registra `CodePagesEncodingProvider` al inicio (Program.cs) y se añade el paquete `System.Text.Encoding.CodePages`.
Esto evita `System.NotSupportedException: No data is available for encoding 1252`.

### v0.2.6 (Section pick fix)

El `deneva.config` incluye tanto:

- Definición de sección en `<configSections>`:
  `<section name="RabbiMQ.My.MySettings" .../>`

- Bloque real de valores en `<applicationSettings>`:
  `<RabbiMQ.My.MySettings> ... <setting name="RabbitMQ_ConnectionString"><value>...</value>`

En v0.2.5 el lector podía quedarse con el nodo `<section name="...">`, lo que hacía que no
aparecieran los `<setting>` y devolviese null.

En v0.2.6 se prioriza el elemento cuyo nombre sea exactamente la sección (bloque real) y se
evita usar el nodo `<section>`.

Prueba:
- breakpoint en Get(section="RabbiMQ.My.MySettings", key="RabbitMQ_ConnectionString")
- ahora debe devolver el valor del `<value>`.
