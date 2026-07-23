# Multi-stage Alpine build for the SimplCalCon API (ADR 0015, 0024).
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore against the central package manifests first for better layer caching.
COPY Directory.Build.props Directory.Packages.props SimplCalCon.slnx ./
COPY src/ src/
RUN dotnet restore src/SimplCalCon.Api/SimplCalCon.Api.csproj

RUN dotnet publish src/SimplCalCon.Api/SimplCalCon.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./

# The aspnet:*-alpine image ships a non-root `app` user (UID/GID 1654) — use it.
USER app
EXPOSE 9080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD wget -q -O - http://localhost:9080/health/ready || exit 1

ENTRYPOINT ["dotnet", "SimplCalCon.Api.dll"]
