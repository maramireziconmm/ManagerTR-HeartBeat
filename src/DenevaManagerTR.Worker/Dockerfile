# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj", "DenevaManagerTR.Worker/"]
COPY ["src/DenevaManagerTR.Core/DenevaManagerTR.Core.csproj", "DenevaManagerTR.Core/"]
COPY ["src/DenevaManagerTR.Application/DenevaManagerTR.Application.csproj", "DenevaManagerTR.Application/"]
COPY ["src/DenevaManagerTR.Infrastructure/DenevaManagerTR.Infrastructure.csproj", "DenevaManagerTR.Infrastructure/"]
COPY ["nuget.config", "./"]

RUN dotnet restore "DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj"

# Copy source code
COPY ["src/", "./"]

# Build
WORKDIR /src/DenevaManagerTR.Worker
RUN dotnet build "DenevaManagerTR.Worker.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DenevaManagerTR.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Environment variables (can be overridden at runtime)
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DENEVA_CONFIG_PATH=deneva.config

ENTRYPOINT ["dotnet", "DenevaManagerTR.Worker.dll"]
