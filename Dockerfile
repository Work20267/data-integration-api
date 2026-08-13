# syntax=docker/dockerfile:1

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy manifests first so the restore layer caches independently of source edits.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/DataIntegration.Api/*.csproj            src/DataIntegration.Api/
COPY src/DataIntegration.Core/*.csproj           src/DataIntegration.Core/
COPY src/DataIntegration.Infrastructure/*.csproj src/DataIntegration.Infrastructure/

RUN dotnet restore src/DataIntegration.Api/DataIntegration.Api.csproj

COPY src/ src/
RUN dotnet publish src/DataIntegration.Api/DataIntegration.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# APP_UID is provided by the base image; run unprivileged.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DataIntegration.Api.dll"]
