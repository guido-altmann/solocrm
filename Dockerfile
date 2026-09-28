# syntax=docker/dockerfile:1

# ---- build: restore (cached layer), publish, create EF migrations bundle ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore only depends on project files and central build settings, so this layer
# is reused as long as no package or project reference changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/dotnet-tools.json .config/
COPY src/SoloCrm.Domain/SoloCrm.Domain.csproj src/SoloCrm.Domain/
COPY src/SoloCrm.Application/SoloCrm.Application.csproj src/SoloCrm.Application/
COPY src/SoloCrm.Infrastructure/SoloCrm.Infrastructure.csproj src/SoloCrm.Infrastructure/
COPY src/SoloCrm.Web/SoloCrm.Web.csproj src/SoloCrm.Web/
RUN dotnet tool restore \
    && dotnet restore src/SoloCrm.Web/SoloCrm.Web.csproj

COPY src/ src/
RUN dotnet publish src/SoloCrm.Web/SoloCrm.Web.csproj -c Release --no-restore -o /app/publish \
    -p:UseAppHost=false

# Without the Blazor framework script the UI renders statically and no button reacts; fail the build instead.
RUN test -f /app/publish/wwwroot/_framework/blazor.web.js

# The bundle only needs a syntactically valid connection string to build the host;
# the real one is read from ConnectionStrings__Crm when the bundle runs.
RUN ConnectionStrings__Crm="Host=build-only" dotnet ef migrations bundle \
        -p src/SoloCrm.Infrastructure -s src/SoloCrm.Web \
        --configuration Release --no-build -o /app/efbundle

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Data Protection keys live on a persistent volume; the directory must be writable for the non-root user.
RUN mkdir -p /app/keys && chown app:app /app/keys
VOLUME /app/keys

COPY --from=build --chown=app:app /app/publish ./
COPY --from=build --chown=app:app /app/efbundle ./efbundle
COPY --chmod=755 deploy/entrypoint.sh ./entrypoint.sh

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net
EXPOSE 8080

USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent --output /dev/null http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["./entrypoint.sh"]
