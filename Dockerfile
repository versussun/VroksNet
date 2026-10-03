# Builds VroksNet.Web (Blazor WebAssembly) and VroksNet.ApiService separately, then combines
# them into a single runtime image: ApiService serves both the mock API and Web's published
# output as static files (see VroksNet.ApiService/Program.cs). One image, one process.
#
# Multi-arch (linux/amd64, linux/arm64): both build stages run on the build machine's own platform
# ($BUILDPLATFORM). Their output doesn't depend on the target architecture — Web is WebAssembly,
# and ApiService is published framework-dependent without a runtime identifier, carrying the
# native librdkafka/SQLite libraries for every architecture under runtimes/. Only the small final
# stage is per-platform, so an arm64 image builds without emulating the .NET SDK.
#
# The image's contract with the outside (ports, paths, variables, labels) is docs/container-contract.md.

# ---- Stage 1: publish VroksNet.Web (Blazor WebAssembly, standalone) ----
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS web-build
WORKDIR /src
COPY Directory.Packages.props VroksNet.slnx ./
COPY src/VroksNet.Web/VroksNet.Web.csproj src/VroksNet.Web/
RUN dotnet restore src/VroksNet.Web/VroksNet.Web.csproj
COPY src/VroksNet.Web/ src/VroksNet.Web/
RUN dotnet publish src/VroksNet.Web/VroksNet.Web.csproj -c Release -o /app/web --no-restore

# ---- Stage 2: publish VroksNet.ApiService (+ its Clean Architecture layer dependencies) ----
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
# The release version (e.g. 1.4.0), stamped into the assembly's informational version.
ARG VERSION=0.0.0-dev
WORKDIR /src
COPY Directory.Packages.props VroksNet.slnx ./
COPY src/VroksNet.ApiService/VroksNet.ApiService.csproj src/VroksNet.ApiService/
COPY src/VroksNet.Application/VroksNet.Application.csproj src/VroksNet.Application/
COPY src/VroksNet.Domain/VroksNet.Domain.csproj src/VroksNet.Domain/
COPY src/VroksNet.Infrastructure/VroksNet.Infrastructure.csproj src/VroksNet.Infrastructure/
COPY src/VroksNet.ServiceDefaults/VroksNet.ServiceDefaults.csproj src/VroksNet.ServiceDefaults/
RUN dotnet restore src/VroksNet.ApiService/VroksNet.ApiService.csproj
COPY src/VroksNet.ApiService/ src/VroksNet.ApiService/
COPY src/VroksNet.Application/ src/VroksNet.Application/
COPY src/VroksNet.Domain/ src/VroksNet.Domain/
COPY src/VroksNet.Infrastructure/ src/VroksNet.Infrastructure/
COPY src/VroksNet.ServiceDefaults/ src/VroksNet.ServiceDefaults/
# Embedded by VroksNet.Infrastructure: the provisioning manifest schema.
COPY docs/schemas/ docs/schemas/
RUN dotnet publish src/VroksNet.ApiService/VroksNet.ApiService.csproj -c Release -o /app/api --no-restore \
    -p:InformationalVersion=$VERSION
# The portable publish carries native libraries (librdkafka, SQLite) for every OS and CPU under
# runtimes/ — about 100 MB of Windows/macOS/s390x/... binaries. Keep only the target's own.
# TARGETARCH is the platform being built for, even though this stage runs on $BUILDPLATFORM.
ARG TARGETARCH
RUN case "$TARGETARCH" in \
        amd64) keep=linux-x64 ;; \
        arm64) keep=linux-arm64 ;; \
        *) echo "Unsupported TARGETARCH '$TARGETARCH'" >&2; exit 1 ;; \
    esac \
    && find /app/api/runtimes -mindepth 1 -maxdepth 1 ! -name "$keep" -exec rm -rf {} + \
    && ls /app/api/runtimes

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG VERSION=0.0.0-dev

# OCI labels; io.vroksnet.contract.version is the version of docs/container-contract.md this image
# implements, which the Aspire hosting package checks before starting it. It must equal
# ContainerContract.Version (scripts/verify-container-contract.sh checks). CI's metadata step sets
# the same OCI keys from the git tag; these are the fallback for local builds.
LABEL org.opencontainers.image.title="VroksNet" \
      org.opencontainers.image.description="Mock server and contract-testing tool for OpenAPI and AsyncAPI specifications" \
      org.opencontainers.image.source="https://github.com/versussun/VroksNet" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="$VERSION" \
      io.vroksnet.contract.version="1"

# tini as PID 1, with dotnet as its child. As PID 1 itself, dotnet can't die from the SIGABRT its
# own abort() raises on an unhandled exception (the kernel drops default-action signals to PID 1),
# so a startup crash spun at 100% CPU forever instead of exiting — invisible to restart policies.
# Under tini it exits with 134 (128 + SIGABRT) as it should, and SIGTERM on `docker stop` is
# forwarded to it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tini \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=api-build /app/api .
COPY --from=web-build /app/web/wwwroot ./wwwroot
# For tooling outside the container (docs/container-contract.md §4); the app uses its embedded copy.
COPY docs/schemas/provisioning-manifest.v1.schema.json ./provisioning-manifest.v1.schema.json

# SQLite file lives on a mounted volume so data survives container recreation (see
# docs/project-brief.md section 3 "Storage: decision"). The directory belongs to the image's
# unprivileged "app" user ($APP_UID, 1654, from the aspnet base image), so a new named volume
# starts out writable by it. A volume created by an older, root-running image is owned by root
# and has to be chown'ed once — see docs/runbook.md, "Upgrade".
RUN mkdir -p /app/data && chown "$APP_UID:$APP_UID" /app/data
VOLUME /app/data
ENV ConnectionStrings__VroksNetDb="Data Source=/app/data/vroksnet.db"

# Provider mode (see ApiService's ProviderPortSetup): the mock at real spec paths on its own port,
# next to the API/Admin UI on 8080 (the aspnet image's ASPNETCORE_HTTP_PORTS default). Same number
# as AppHost's pinned provider endpoint, so it's 7353 everywhere. CORS on it stays off unless the
# container is run with -e Provider__CorsOrigins=<origins, or *>.
ENV Provider__Port=7353
EXPOSE 8080 7353

# Everything above runs as root (apt, chown); the app itself doesn't. Both ports are above 1024,
# so binding them needs no privileges.
USER $APP_UID

ENTRYPOINT ["/usr/bin/tini", "--", "dotnet", "VroksNet.ApiService.dll"]
