# DenevaManagerTR – .NET 9 Migration

Worker service for Deneva Transit Manager with support for HeartBeat, InfoStation, and InfoLineasEstado operations.

## 🚀 What's New in v1.0 (.NET 9 Migration)

- **Migrated to .NET 9** with WebApplicationBuilder
- **HTTP-based configuration**: Download `deneva.config` from remote URL with fallback to local file
- **Integrated Artifact.Transit.Logging** with audit logging enabled by default
- **Environment variable configuration** for flexibility in containerized deployments
- **Docker & Docker Compose** support with comprehensive examples
- **Modular DI architecture** with ServiceExtensions

---

## 📋 Requirements

- **.NET 9 SDK** or later
- **Access to private NuGet feed** (Azure DevOps): `https://devops.int.iconmm.com/.../gitconhub/nuget/v3/index.json`
- **deneva.config** file or HTTP endpoint with configuration
- **RabbitMQ** server
- **MySQL** database (5.7 or 8.0)

---

## 🔧 Configuration

### Environment Variables

#### Deneva Configuration (HTTP Download)

- **`DENEVA_CONFIG_URL`**: URL to download `deneva.config` via HTTP
  - Example: `https://config-server.example.com/deneva.config`
  - If set, the application will attempt to download configuration from this URL
  - Falls back to local file if download fails

- **`DENEVA_CONFIG_AUTHORIZATION`**: Authorization header for HTTP download (optional)
  - Example: `Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...`
  - Takes priority over `DenevaConfig:Authorization` in appsettings.json
  - Used when the config endpoint requires authentication

- **`DenevaConfig__Path`**: Local file path for `deneva.config` (fallback)
  - Default: `deneva.config`
  - Used when HTTP download fails or is not configured
  - Example: `/app/config/deneva.config`

#### Logging

- **`SERILOG_PATH`**: Directory path for log files
  - Default: `logs`
  - Application writes structured logs with daily rotation
  - Example: `/app/logs` (for Docker containers)

#### Application Settings

- **`RunMode`**: Execution mode
  - Values: `Heartbeat`, `InfoStation`, `InfoLineasEstado`
  - Default: `Heartbeat`
  - Determines which services are activated

### Configuration Files

#### appsettings.json

```json
{
  "RunMode": "InfoLineasEstado",
  "DenevaConfig": {
    "Path": "C:\\Deneva\\Resources\\Config\\deneva.config",
    "Url": "https://config-server.example.com/deneva.config",
    "Authorization": "Bearer token-from-config",
    "RabbitSection": "RabbitMQ.My.MySettings"
  },
  "Database": {
    "Engine": "mysql57"
  },
  "Jobs": {
    "Heartbeat": {
      "Enabled": true,
      "IntervalSeconds": 30
    }
  }
}
```

#### deneva.config

XML configuration file containing:
- RabbitMQ connection settings
- Database connection strings
- Application-specific settings

---

## 🐳 Docker Deployment

### Using Docker Compose (Recommended)

```bash
# Set environment variables (optional)
export DENEVA_CONFIG_URL=https://your-config-server.com/deneva.config
export DENEVA_CONFIG_AUTHORIZATION="Bearer your-token"
export SERILOG_PATH=/app/logs

# Start the service
docker-compose up -d

# View logs
docker-compose logs -f

# Stop the service
docker-compose down
```

### Using Dockerfile directly

```bash
# Build image
docker build -t deneva-manager-worker -f src/DenevaManagerTR.Worker/Dockerfile .

# Run container
docker run -d \
  -e DENEVA_CONFIG_URL=https://your-config-server.com/deneva.config \
  -e DENEVA_CONFIG_AUTHORIZATION="Bearer your-token" \
  -e RunMode=InfoLineasEstado \
  -v $(pwd)/config:/app/config:ro \
  -v $(pwd)/logs:/app/logs \
  --name deneva-worker \
  deneva-manager-worker
```

---

## 🛠️ Development

### Build & Run Locally

```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run Worker
dotnet run --project src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj
```

### Testing Configuration Download

Set environment variables before running:

```bash
# Windows (PowerShell)
$env:DENEVA_CONFIG_URL="https://your-server.com/deneva.config"
$env:DENEVA_CONFIG_AUTHORIZATION="Bearer your-token"
dotnet run --project src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj

# Linux/macOS (Bash)
export DENEVA_CONFIG_URL=https://your-server.com/deneva.config
export DENEVA_CONFIG_AUTHORIZATION="Bearer your-token"
dotnet run --project src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj
```

---

## 🔐 CI/CD Configuration

### GitHub Actions Setup

The project uses a **private NuGet feed** hosted on Azure DevOps. To configure CI/CD:

#### 1. Add Repository Secrets

Go to **Settings → Secrets and variables → Actions** and add:

- **`NUGET_USER`**: Your Azure DevOps username or PAT name
- **`NUGET_PAT`**: Your Azure DevOps Personal Access Token (with Package Read permissions)

Or use a single token:

- **`NUGET_FEED_TOKEN`**: Personal Access Token with Package Read permissions

#### 2. GitHub Actions Workflow Example

Create `.github/workflows/build.yml`:

```yaml
name: Build and Test

on:
  push:
    branches: [ main, feature/** ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET 9
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
    
    - name: Authenticate to Private Feed
      run: |
        dotnet nuget add source \
          "https://devops.int.iconmm.com/DefaultCollection/95935141-2068-42a2-9352-0dd16f72a1af/_packaging/gitconhub/nuget/v3/index.json" \
          --name gitconhub \
          --username ${{ secrets.NUGET_USER }} \
          --password ${{ secrets.NUGET_PAT }} \
          --store-password-in-clear-text
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --configuration Release --no-restore
    
    - name: Test
      run: dotnet test --configuration Release --no-build --verbosity normal
```

#### 3. Alternative: Using nuget.config with tokens

If using Azure DevOps PAT, update `nuget.config` credentials during CI:

```yaml
    - name: Configure NuGet credentials
      run: |
        dotnet nuget update source gitconhub \
          --username az \
          --password ${{ secrets.NUGET_FEED_TOKEN }} \
          --store-password-in-clear-text \
          --configfile nuget.config
```

---

## 📚 Architecture

### Run Modes

1. **Heartbeat**: Periodic heartbeat publisher
2. **InfoStation**: RabbitMQ consumer for `GETINFOSTATION` messages
3. **InfoLineasEstado**: RabbitMQ consumer for `GETINFOLINEASESTADOS` messages

### Service Layers

- **Core**: Domain entities and interfaces
- **Application**: Business logic and application services
- **Infrastructure**: External dependencies (DB, RabbitMQ, HTTP, Crypto)
- **Worker**: Host application with DI configuration

### Configuration Hierarchy

1. **HTTP Download** (if `DENEVA_CONFIG_URL` is set)
   - Uses `IHttpClientFactory` with named client "DenevaConfigClient"
   - Adds `Authorization` header from environment variable or appsettings
   - Caches content by SHA256 hash
   - Reloads only when content changes

2. **Local File Fallback** (if HTTP fails or not configured)
   - Reads from `DenevaConfig:Path` or `DENEVA_CONFIG_PATH`
   - Monitors file changes by LastWriteTimeUtc

---

## 📝 Version History

### v1.0 – .NET 9 Migration & HTTP Configuration

- Migrated to .NET 9 and WebApplicationBuilder
- Added HTTP-based configuration download with authentication
- Integrated Artifact.Transit.Logging with audit support
- Extracted DI to ServiceExtensions for modularity
- Added Docker and docker-compose support
- Updated all dependencies to .NET 9 versions

### v0.3.9 – InfoStation (consumidor + respuesta)

Esta entrega compila por defecto **solo** la funcionalidad de InfoStation.

#### Selección de funcionalidad (compile-time)

En `Directory.Build.props`:

- `DENEVA_INFOSTATION` -> compila y registra el consumidor de RabbitMQ para `GETINFOSTATION` y publica `SYNCINFOSTATION_RETURN`.
- `DENEVA_HEARTBEAT` -> compila y registra el job de HeartBeat.

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

---

## 🤝 Contributing

1. Create a feature branch from `main`
2. Make your changes following the existing code style
3. Ensure all tests pass
4. Submit a Pull Request

---

## 📄 License

Internal ICONMM project. All rights reserved.

