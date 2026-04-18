# syntax=docker/dockerfile:1.7
# BookSlot.Web — Blazor Web App (Server + WASM client). Multi-stage build.

ARG DOTNET_SDK_VERSION=10.0
ARG DOTNET_RUNTIME_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src

COPY BookSlot.slnx Directory.Packages.props Directory.Build.props global.json ./
COPY src/BookSlot.Domain/BookSlot.Domain.csproj                 src/BookSlot.Domain/
COPY src/BookSlot.Infrastructure/BookSlot.Infrastructure.csproj src/BookSlot.Infrastructure/
COPY src/BookSlot.Features/BookSlot.Features.csproj             src/BookSlot.Features/
COPY src/BookSlot.Web.Client/BookSlot.Web.Client.csproj         src/BookSlot.Web.Client/
COPY src/BookSlot.Web/BookSlot.Web.csproj                       src/BookSlot.Web/

RUN dotnet restore src/BookSlot.Web/BookSlot.Web.csproj

COPY src/BookSlot.Domain/         src/BookSlot.Domain/
COPY src/BookSlot.Infrastructure/ src/BookSlot.Infrastructure/
COPY src/BookSlot.Features/       src/BookSlot.Features/
COPY src/BookSlot.Web.Client/     src/BookSlot.Web.Client/
COPY src/BookSlot.Web/            src/BookSlot.Web/

RUN dotnet publish src/BookSlot.Web/BookSlot.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

RUN groupadd --system bookslot && useradd --system --gid bookslot --create-home bookslot
USER bookslot

COPY --from=build --chown=bookslot:bookslot /app/publish ./

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget --quiet --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "BookSlot.Web.dll"]
