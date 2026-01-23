# Pull Request Description

## Title
Migrate to WebApplicationBuilder (.NET 9), Deneva HTTP config and Artifact.Logging

## Type
- [x] Feature
- [x] Enhancement
- [ ] Bug Fix
- [x] Breaking Change
- [x] Documentation Update

## Description
This PR migrates the DenevaManagerTR application to .NET 9 with the modern WebApplicationBuilder pattern, adds HTTP-based configuration fetching capability, and prepares for Artifact.Transit.Logging integration.

## Changes Made

### 1. .NET 9 Migration
- Updated all project files (`Core`, `Infrastructure`, `Application`, `Worker`) to target `net9.0`
- Updated Microsoft.Extensions packages to version 9.*
- Changed Worker project from `Microsoft.NET.Sdk.Worker` to `Microsoft.NET.Sdk.Web` for WebApplication support

### 2. WebApplicationBuilder Pattern
- **Before**: Used `Host.CreateDefaultBuilder(args)`
- **After**: Uses `WebApplication.CreateBuilder(args)`
- Benefits:
  - Modern ASP.NET Core hosting model
  - Better integration with ASP.NET Core middleware
  - Simplified configuration
  - Better support for web-based health checks and metrics

### 3. Centralized DI Registration
- Created `ServiceExtensions.cs` in Infrastructure project
- Moved common service registrations to `AddInfrastructureServices()` extension method
- Cleaner Program.cs with better separation of concerns

### 4. HTTP-based Deneva Config Fetching
Enhanced `DenevaConfigReader` with:
- HTTP fetching using `IHttpClientFactory`
- SHA256 hash-based caching (only reloads if content changes)
- Authorization header support (from config or `DENEVA_CONFIG_AUTHORIZATION` environment variable)
- Graceful fallback to local file if HTTP fetch fails
- Thread-safe implementation

Configuration priority:
1. Try HTTP fetch from `DENEVA_CONFIG_URL` (if set)
2. Fall back to local file at `DenevaConfig:Path`

### 5. Docker Support
- **Dockerfile**: Multi-stage build using .NET 9 SDK and ASP.NET 9 runtime
- **docker-compose.yml**: Example deployment configuration with environment variables

### 6. Documentation
Updated `README.md` with:
- v0.4.0 release notes
- Deployment environment variables table
- Docker deployment instructions
- CI/CD considerations for private NuGet feeds
- Configuration priority documentation

### 7. Artifact.Transit.Logging (Prepared)
- Package references added but **commented out** (not available on nuget.org)
- Code prepared in Program.cs with `AddTransitLogging` (commented)
- TODO comments added for when private feed is configured

## Files to Review

### Critical Files
- `src/DenevaManagerTR.Worker/Program.cs` - New hosting pattern
- `src/DenevaManagerTR.Infrastructure/ServiceExtensions.cs` - DI centralization
- `src/DenevaManagerTR.Infrastructure/Config/DenevaConfigReader.cs` - HTTP fetching logic

### Configuration Files
- `Dockerfile` - Container build configuration
- `docker-compose.yml` - Deployment example
- `.gitignore` - Build artifacts exclusion

### Documentation
- `README.md` - Updated deployment docs
- `MIGRATION_COMPLETE.md` - Migration summary

### Project Files
- `src/DenevaManagerTR.Core/DenevaManagerTR.Core.csproj`
- `src/DenevaManagerTR.Infrastructure/DenevaManagerTR.Infrastructure.csproj`
- `src/DenevaManagerTR.Application/DenevaManagerTR.Application.csproj`
- `src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj`

## Testing

### Build Validation
✅ All projects build successfully with `dotnet build`
- 0 Warnings
- 0 Errors

### Manual Testing Required
Since this is a WIP/Draft PR, please test:
1. Build with private NuGet feed (uncomment Artifact.Transit.Logging)
2. Run locally with HTTP config fetching
3. Run locally with local file fallback
4. Docker build and run
5. Verify all three run modes: Heartbeat, InfoStation, InfoLineasEstado

## Deployment Configuration

### Environment Variables

| Variable | Required | Description | Default |
|----------|----------|-------------|---------|
| `RunMode` | No | Execution mode | `Heartbeat` |
| `DENEVA_CONFIG_URL` | No | HTTP endpoint for config | (uses local file) |
| `DENEVA_CONFIG_AUTHORIZATION` | Conditional | Auth header for HTTP | (uses config value) |
| `DenevaConfig__Path` | No | Local config file path | `C:\Deneva\Resources\Config\deneva.config` |

## CI/CD Considerations

### Private NuGet Feed
⚠️ **Artifact.Transit.Logging** is hosted on a private feed and is currently commented out.

**Before merging, the CI/CD pipeline needs:**
1. `nuget.config` added to repository
2. Feed credentials configured as secrets:
   - `NUGET_SOURCE_URL`
   - `NUGET_SOURCE_USERNAME`
   - `NUGET_SOURCE_PASSWORD`
3. Uncomment package references in all .csproj files
4. Uncomment `AddTransitLogging` call in Program.cs

## Breaking Changes
- Requires .NET 9 runtime (was .NET 8)
- Worker project now uses Web SDK instead of Worker SDK
- New environment variable `DENEVA_CONFIG_URL` and `DENEVA_CONFIG_AUTHORIZATION` (optional)

## Rollback Plan
If issues arise:
1. Revert this PR
2. Application will return to .NET 8 with Host.CreateDefaultBuilder
3. Config will only load from local files

## Next Steps
1. ✅ Code review
2. ⏳ Configure private NuGet feed access
3. ⏳ Test in staging environment
4. ⏳ Uncomment Artifact.Transit.Logging when ready
5. ⏳ Mark PR as ready for merge
6. ⏳ Deploy to production

## Checklist
- [x] Code follows project style guidelines
- [x] Self-review completed
- [x] Code builds successfully
- [x] Documentation updated
- [x] Breaking changes documented
- [ ] Tested with Artifact.Transit.Logging (requires private feed)
- [ ] Tested in Docker container
- [ ] Tested all run modes
- [ ] CI/CD pipeline configured

## Related Issues
Implements feature request for .NET 9 migration, WebApplicationBuilder adoption, and HTTP-based configuration management.
