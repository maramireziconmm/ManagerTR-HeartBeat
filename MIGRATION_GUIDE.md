# Migration Guide: .NET 8 to .NET 9 with WebApplicationBuilder

## Overview

This guide provides step-by-step instructions for migrating existing deployments from the previous version (v0.3.9, .NET 8, Host.CreateDefaultBuilder) to v1.0.0 (.NET 9, WebApplicationBuilder, HTTP config).

## Prerequisites

- .NET 9 Runtime installed (for local deployments)
- Docker with .NET 9 support (for containerized deployments)
- Access to private NuGet feed if using Artifact.Transit.Logging (optional)

## Migration Steps

### Step 1: Update Runtime Environment

#### Option A: Local Deployment

1. Install .NET 9 Runtime:
   ```bash
   # Download from: https://dotnet.microsoft.com/download/dotnet/9.0
   # Or use package manager
   sudo apt-get update
   sudo apt-get install -y dotnet-runtime-9.0
   ```

2. Verify installation:
   ```bash
   dotnet --list-runtimes | grep 9.0
   ```

#### Option B: Docker Deployment

Update your Docker images to use .NET 9 base images:
- SDK: `mcr.microsoft.com/dotnet/sdk:9.0`
- Runtime: `mcr.microsoft.com/dotnet/aspnet:9.0`

### Step 2: Configure New Environment Variables

Add the following environment variables to your deployment configuration:

#### Required for HTTP Config (New Feature)

```bash
# URL to download deneva.config
DENEVA_CONFIG_URL=https://config-server.example.com/deneva.config

# Authorization header for config download
DENEVA_CONFIG_AUTHORIZATION=Bearer your-token-here
```

#### Optional

```bash
# Serilog log path (default: /app/logs)
SERILOG_PATH=/app/logs

# Run mode (Heartbeat, InfoStation, InfoLineasEstado)
RunMode=InfoLineasEstado
```

### Step 3: Update Configuration Files

#### appsettings.json

Add new configuration sections:

```json
{
  "RunMode": "InfoLineasEstado",
  "DenevaConfig": {
    "Path": "C:\\Deneva\\Resources\\Config\\deneva.config",
    "Url": null,  // Or specify URL here instead of env var
    "Authorization": null,  // Or specify here instead of env var
    "RabbitSection": "RabbitMQ.My.MySettings"
  },
  "SerilogConfiguration": {
    "PathLog": "/app/logs"
  },
  "Database": {
    "Engine": "mysql57"
  },
  "Jobs": {
    // ... existing job configurations
  }
}
```

### Step 4: Deploy

#### Option A: Docker Compose (Recommended)

1. Create `.env` file:
   ```bash
   DENEVA_CONFIG_URL=https://config-server.example.com/deneva.config
   DENEVA_CONFIG_AUTHORIZATION=Bearer your-token-here
   SERILOG_PATH=/app/logs
   ```

2. Deploy:
   ```bash
   docker-compose pull
   docker-compose up -d
   ```

3. Verify:
   ```bash
   docker-compose logs -f denevamanagertr-worker
   ```

#### Option B: Direct Deployment

1. Stop existing service:
   ```bash
   sudo systemctl stop denevamanagertr
   ```

2. Deploy new binaries:
   ```bash
   dotnet publish -c Release -o /opt/denevamanagertr
   ```

3. Update systemd service file:
   ```ini
   [Service]
   Environment="DENEVA_CONFIG_URL=https://config-server.example.com/deneva.config"
   Environment="DENEVA_CONFIG_AUTHORIZATION=Bearer your-token-here"
   Environment="SERILOG_PATH=/var/log/denevamanagertr"
   ExecStart=/usr/bin/dotnet /opt/denevamanagertr/DenevaManagerTR.Worker.dll
   ```

4. Start service:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl start denevamanagertr
   ```

### Step 5: Verify Migration

#### Check Application Startup

1. Review logs for successful startup:
   ```bash
   # Docker
   docker-compose logs -f denevamanagertr-worker
   
   # Systemd
   sudo journalctl -u denevamanagertr -f
   ```

2. Look for these success indicators:
   - `Artifact.Transit.Logging configured` or `Fallback logging configured`
   - `deneva.config cargado desde URL` or `deneva.config cargado desde fichero`
   - HostedService startup messages

#### Verify Configuration Loading

**If using HTTP config:**
- Check logs for: `deneva.config cargado desde URL. Url=...`
- If URL fails, verify: `deneva.config cargado desde fichero. Path=...`

**If using local file:**
- Check logs for: `deneva.config cargado desde fichero. Path=...`

#### Verify HostedServices

Check that the appropriate HostedService is running based on RunMode:
- **Heartbeat**: Look for heartbeat messages in RabbitMQ
- **InfoStation**: Verify InfoStation consumer is active
- **InfoLineasEstado**: Verify InfoLineasEstado consumer is active

#### Verify Logging

1. Check log files are being created:
   ```bash
   ls -lh $SERILOG_PATH
   ```

2. Expected files:
   - `DenevaManagerTR.Heartbeat-YYYYMMDD.log`
   - `DenevaManagerTR.Heartbeat.RabbitMQ-YYYYMMDD.log`
   - Audit logs (if Artifact.Transit.Logging is enabled)

## Rollback Procedure

If issues occur, follow these steps to rollback:

### Docker Deployment

1. Stop current version:
   ```bash
   docker-compose down
   ```

2. Change image tag to previous version in `docker-compose.yml`:
   ```yaml
   services:
     denevamanagertr-worker:
       image: denevamanagertr:v0.3.9  # Previous version
   ```

3. Restart:
   ```bash
   docker-compose up -d
   ```

### Direct Deployment

1. Stop service:
   ```bash
   sudo systemctl stop denevamanagertr
   ```

2. Restore previous binaries from backup:
   ```bash
   sudo cp -r /opt/denevamanagertr.backup/* /opt/denevamanagertr/
   ```

3. Revert systemd service file changes

4. Start service:
   ```bash
   sudo systemctl start denevamanagertr
   ```

## Troubleshooting

### Issue: Application won't start

**Symptoms:** Container/service starts then immediately exits

**Solutions:**
1. Check logs for specific error
2. Verify .NET 9 runtime is available
3. Verify all required configuration is present
4. Check deneva.config is accessible (URL or file)

### Issue: Config not loading from URL

**Symptoms:** Log shows "No se pudo cargar deneva.config desde URL"

**Solutions:**
1. Verify `DENEVA_CONFIG_URL` is set correctly
2. Check `DENEVA_CONFIG_AUTHORIZATION` has valid credentials
3. Test URL manually: `curl -H "Authorization: Bearer token" $DENEVA_CONFIG_URL`
4. Ensure firewall allows outbound HTTPS
5. Verify fallback to local file works

### Issue: Artifact.Transit.Logging errors

**Symptoms:** Errors about Artifact.Transit.Logging assembly

**Solutions:**
1. This is expected if package is not available
2. Application will use fallback logging automatically
3. To enable: Uncomment package reference and configure nuget.config

### Issue: HostedService not starting

**Symptoms:** No messages being processed

**Solutions:**
1. Verify `RunMode` is set correctly
2. Check RabbitMQ connection string in deneva.config
3. Verify RabbitMQ is accessible
4. Check logs for connection errors

### Issue: Build/Restore failures

**Symptoms:** `Unable to find package Artifact.Transit.Logging`

**Solutions:**
1. Package is optional - commented out by default
2. To use: Uncomment in `DenevaManagerTR.Worker.csproj`
3. Configure private NuGet feed in `nuget.config`
4. Set credentials as environment variables

## Configuration Migration Matrix

| Old Configuration | New Configuration | Notes |
|-------------------|-------------------|-------|
| `DenevaConfig:Path` | `DenevaConfig:Path` | Still supported (fallback) |
| N/A | `DenevaConfig:Url` | New: HTTP config URL |
| N/A | `DenevaConfig:Authorization` | New: Auth header |
| N/A | `DENEVA_CONFIG_URL` | New: Env var (preferred) |
| N/A | `DENEVA_CONFIG_AUTHORIZATION` | New: Env var (preferred) |
| N/A | `SERILOG_PATH` | New: Log path env var |
| N/A | `SerilogConfiguration:PathLog` | New: Log path in config |
| `RunMode` | `RunMode` | Unchanged |
| `Jobs:*` | `Jobs:*` | Unchanged |
| `Database:Engine` | `Database:Engine` | Unchanged |

## Testing Checklist

After migration, verify:

- [ ] Application starts successfully
- [ ] Logs are being written to expected location
- [ ] Configuration loaded (URL or file)
- [ ] HostedService is running
- [ ] Messages are being processed (if applicable)
- [ ] RabbitMQ connection is established
- [ ] Database connection works
- [ ] No errors in logs
- [ ] Resource usage is normal (CPU, Memory)

## Support

For issues or questions during migration:

1. Check logs first: `docker-compose logs -f` or `journalctl -u denevamanagertr -f`
2. Review troubleshooting section above
3. Refer to README.md for configuration details
4. Contact development team

## Additional Resources

- [README.md](README.md) - Full documentation
- [PR_DESCRIPTION.md](PR_DESCRIPTION.md) - Detailed changes
- [.NET 9 Migration Guide](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9)
- [Docker Compose Documentation](https://docs.docker.com/compose/)

---

**Last Updated:** 2026-01-22
**Version:** 1.0.0
