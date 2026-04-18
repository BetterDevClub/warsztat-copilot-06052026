# syntax=docker/dockerfile:1.7
# BookSlot.Api — multi-stage build for the public/admin REST API.

ARG DOTNET_SDK_VERSION=10.0
ARG DOTNET_RUNTIME_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src

# Copy solution + central package config first so restore can be cached.
COPY BookSlot.slnx Directory.Packages.props Directory.Build.props global.json ./
COPY src/BookSlot.Domain/BookSlot.Domain.csproj                     src/BookSlot.Domain/
COPY src/BookSlot.Infrastructure/BookSlot.Infrastructure.csproj     src/BookSlot.Infrastructure/
COPY src/BookSlot.Features/BookSlot.Features.csproj                 src/BookSlot.Features/
COPY src/BookSlot.Api/BookSlot.Api.csproj                           src/BookSlot.Api/

RUN dotnet restore src/BookSlot.Api/BookSlot.Api.csproj

# Now copy the rest of the source.
COPY src/BookSlot.Domain/         src/BookSlot.Domain/
COPY src/BookSlot.Infrastructure/ src/BookSlot.Infrastructure/
COPY src/BookSlot.Features/       src/BookSlot.Features/
COPY src/BookSlot.Api/            src/BookSlot.Api/

RUN dotnet publish src/BookSlot.Api/BookSlot.Api.csproj \
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

# Create non-root user.
RUN groupadd --system bookslot && useradd --system --gid bookslot --create-home bookslot
USER bookslot

COPY --from=build --chown=bookslot:bookslot /app/publish ./

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget --quiet --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "BookSlot.Api.dll"]
