# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj", "DenevaManagerTR.Worker/"]
COPY ["src/DenevaManagerTR.Infrastructure/DenevaManagerTR.Infrastructure.csproj", "DenevaManagerTR.Infrastructure/"]
COPY ["src/DenevaManagerTR.Application/DenevaManagerTR.Application.csproj", "DenevaManagerTR.Application/"]
COPY ["src/DenevaManagerTR.Core/DenevaManagerTR.Core.csproj", "DenevaManagerTR.Core/"]
COPY ["Directory.Build.props", "./"]

RUN dotnet restore "DenevaManagerTR.Worker/DenevaManagerTR.Worker.csproj"

# Copy all source code
COPY src/ .

# Build and publish
WORKDIR "/src/DenevaManagerTR.Worker"
RUN dotnet build "DenevaManagerTR.Worker.csproj" -c Release -o /app/build
RUN dotnet publish "DenevaManagerTR.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy DenevaCrypto.dll to the runtime image
COPY --from=build /src/DenevaManagerTR.Worker/DenevaCrypto.dll ./

# Copy published application
COPY --from=build /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

ENTRYPOINT ["dotnet", "DenevaManagerTR.Worker.dll"]
