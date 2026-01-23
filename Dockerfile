# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["DenevaManagerTR.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/DenevaManagerTR.Core/DenevaManagerTR.Core.csproj", "src/DenevaManagerTR.Core/"]
COPY ["src/DenevaManagerTR.Infrastructure/DenevaManagerTR.Infrastructure.csproj", "src/DenevaManagerTR.Infrastructure/"]
COPY ["src/DenevaManagerTR.Application/DenevaManagerTR.Application.csproj", "src/DenevaManagerTR.Application/"]
COPY ["src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj", "src/DenevaManagerTR.Worker/"]

# Restore dependencies
RUN dotnet restore "src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj"

# Copy everything else
COPY . .

# Build and publish
WORKDIR "/src/src/DenevaManagerTR.Worker"
RUN dotnet build "DenevaManagerTR.Worker.csproj" -c Release -o /app/build
RUN dotnet publish "DenevaManagerTR.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Copy DenevaCrypto.dll if needed
COPY --from=build /src/src/DenevaManagerTR.Worker/DenevaCrypto.dll .

# Set environment variables (can be overridden at runtime)
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "DenevaManagerTR.Worker.dll"]
