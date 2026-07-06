# syntax=docker/dockerfile:1
# Multi-stage build for the CacheScope Host. Build context is the src/ directory
# so all referenced projects are available to `dotnet restore`/`publish`.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first for layer-cached restore.
COPY CacheScope.slnx ./
COPY Directory.Build.props ./
COPY CacheScope.Host/CacheScope.Host.csproj CacheScope.Host/
COPY CacheScope.Api/CacheScope.Api.csproj CacheScope.Api/
COPY CacheScope.Shared/CacheScope.Shared.csproj CacheScope.Shared/
COPY CacheScope.MemoryCache/CacheScope.MemoryCache.csproj CacheScope.MemoryCache/
COPY CacheScope.RedisCache/CacheScope.RedisCache.csproj CacheScope.RedisCache/
COPY CacheScope.Cloudflare/CacheScope.Cloudflare.csproj CacheScope.Cloudflare/
COPY CacheScope.Database/CacheScope.Database.csproj CacheScope.Database/
COPY CacheScope.Analytics/CacheScope.Analytics.csproj CacheScope.Analytics/
COPY CacheScope.TrafficGenerator/CacheScope.TrafficGenerator.csproj CacheScope.TrafficGenerator/
COPY CacheScope.Realtime/CacheScope.Realtime.csproj CacheScope.Realtime/
RUN dotnet restore CacheScope.Host/CacheScope.Host.csproj

# Copy the rest of the source and publish.
COPY . .
RUN dotnet publish CacheScope.Host/CacheScope.Host.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Container Apps injects PORT; Kestrel binds to it.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CacheScope.Host.dll"]
