# DenevaManagerTR – v0.2.6 (Section pick fix)

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


## v0.2.10 – Soporte Encoding 1252 en .NET 8

Se registra `CodePagesEncodingProvider` al inicio (Program.cs) y se añade el paquete `System.Text.Encoding.CodePages`.
Esto evita `System.NotSupportedException: No data is available for encoding 1252`.


## v0.3.3
- HeartBeat: Header OriginSystem/ContentType y routingKey desde appsettings (Jobs:Heartbeat).
- Logging a fichero: logs/DenevaManagerTR.Heartbeat-YYYYMMDD.log y logs/DenevaManagerTR.Heartbeat.RabbitMQ-YYYYMMDD.log.


## v0.3.9 – InfoStation (consumidor + respuesta)

Esta entrega compila por defecto **solo** la funcionalidad de InfoStation.

### Selección de funcionalidad (compile-time)

En `Directory.Build.props`:

- `DENEVA_INFOSTATION` -> compila y registra el consumidor de RabbitMQ para `GETINFOSTATION` y publica `SYNCINFOSTATION_RETURN`.
- `DENEVA_HEARTBEAT` -> compila y registra el job de HeartBeat.

Para cambiarlo, sustituye el símbolo en `DefineConstants`.

### Configuración

- Exchange de entrada/salida InfoStation: `RabbitMQ_InfoEstacionExchangeName` (leído desde `deneva.config`).
- Conexión RabbitMQ: `RabbitMQ_ConnectionString`, `RabbitMQ_Port`, `RabbitMQ_User`, `RabbitMQ_Pass`, `RabbitMQ_Host`, `RabbitMQ_VHost` (según tu `deneva.config`).
- Parámetros del consumidor: `Jobs:InfoStation` en `appsettings.json` (cola, topics y AppId).

---

## v0.4.0 – Migration to .NET 9, WebApplicationBuilder, and Artifact.Transit.Logging

This version migrates the application to:
- **.NET 9.0** runtime
- **WebApplicationBuilder** pattern (from Host.CreateDefaultBuilder)
- **Artifact.Transit.Logging** for centralized logging with audit support
- **HTTP-based deneva.config fetching** with local file fallback

### Deployment Configuration

The application supports both local and HTTP-based configuration fetching:

#### Environment Variables

| Variable | Description | Required | Default |
|----------|-------------|----------|---------|
| `RunMode` | Execution mode: `Heartbeat`, `InfoStation`, or `InfoLineasEstado` | No | `Heartbeat` |
| `DENEVA_CONFIG_URL` | HTTP endpoint to fetch deneva.config | No | (uses local file) |
| `DENEVA_CONFIG_AUTHORIZATION` | Authorization header value for HTTP request (e.g., `Bearer token123`) | If using HTTP | (falls back to `DenevaConfig:Authorization`) |
| `DenevaConfig__Path` | Local path to deneva.config file (fallback) | No | `C:\Deneva\Resources\Config\deneva.config` |
| `DenevaConfig__Authorization` | Authorization header from appsettings (fallback to env var) | No | (none) |
| `DenevaConfig__RabbitSection` | Section name in deneva.config for RabbitMQ settings | No | `RabbitMQ.My.MySettings` |

#### Configuration Priority

1. **HTTP Fetch (if `DENEVA_CONFIG_URL` is set)**:
   - Fetches config from URL with Authorization header
   - Caches content by SHA256 hash (only reloads if changed)
   - Falls back to local file if HTTP fetch fails

2. **Local File (if HTTP not configured or fails)**:
   - Reads from `DenevaConfig__Path`
   - Monitors file changes (reloads when modified)

### Docker Deployment

#### Build and Run with Docker Compose

```bash
# Build and start
docker-compose up -d

# View logs
docker-compose logs -f

# Stop
docker-compose down
```

#### Build Docker Image Manually

```bash
docker build -t deneva-manager-tr:latest .
```

#### Run with Environment Variables

```bash
docker run -d \
  --name deneva-manager-tr \
  -e RunMode=InfoLineasEstado \
  -e DENEVA_CONFIG_URL=https://config.example.com/deneva.config \
  -e DENEVA_CONFIG_AUTHORIZATION="Bearer your-token-here" \
  -v $(pwd)/logs:/app/logs \
  deneva-manager-tr:latest
```

### CI/CD Considerations

**⚠️ Private Package Feed Notice**

This project uses `Artifact.Transit.Logging` which may be hosted on a private NuGet feed.

For CI/CD pipelines:
1. Add `nuget.config` to the repository with package source configuration
2. Configure feed credentials as secrets/environment variables:
   - `NUGET_SOURCE_URL` - The private feed URL
   - `NUGET_SOURCE_USERNAME` - Authentication username
   - `NUGET_SOURCE_PASSWORD` - Authentication password/PAT

Example `nuget.config`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="PrivateFeed" value="YOUR_PRIVATE_FEED_URL" />
  </packageSources>
  <packageSourceCredentials>
    <PrivateFeed>
      <add key="Username" value="%NUGET_SOURCE_USERNAME%" />
      <add key="ClearTextPassword" value="%NUGET_SOURCE_PASSWORD%" />
    </PrivateFeed>
  </packageSourceCredentials>
</configuration>
```

### Development

#### Build

```bash
dotnet build DenevaManagerTR.sln
```

#### Run Locally

```bash
cd src/DenevaManagerTR.Worker
dotnet run
```

#### Test with HTTP Config

```bash
export DENEVA_CONFIG_URL=https://your-config-server/deneva.config
export DENEVA_CONFIG_AUTHORIZATION="Bearer your-token"
dotnet run
```
