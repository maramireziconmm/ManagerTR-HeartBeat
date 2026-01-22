# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj", "src/DenevaManagerTR.Worker/"]
COPY ["src/DenevaManagerTR.Infrastructure/DenevaManagerTR.Infrastructure.csproj", "src/DenevaManagerTR.Infrastructure/"]
COPY ["src/DenevaManagerTR.Application/DenevaManagerTR.Application.csproj", "src/DenevaManagerTR.Application/"]
COPY ["src/DenevaManagerTR.Core/DenevaManagerTR.Core.csproj", "src/DenevaManagerTR.Core/"]
COPY ["Directory.Build.props", "./"]

# Restore dependencies
RUN dotnet restore "src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/src/DenevaManagerTR.Worker"
RUN dotnet build "DenevaManagerTR.Worker.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DenevaManagerTR.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create logs directory
RUN mkdir -p /app/logs

# Copy published app
COPY --from=publish /app/publish .

# Copy DenevaCrypto.dll if needed
COPY --from=build /src/src/DenevaManagerTR.Worker/DenevaCrypto.dll ./

# Set environment variables (can be overridden)
ENV ASPNETCORE_ENVIRONMENT=Production
ENV SERILOG_PATH=/app/logs
ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "DenevaManagerTR.Worker.dll"]
