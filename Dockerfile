# Builds VroksNet.Web (Blazor WebAssembly) and VroksNet.ApiService separately, then combines
# them into a single runtime image: ApiService serves both the mock API and Web's published
# output as static files (see VroksNet.ApiService/Program.cs). One image, one process.

# ---- Stage 1: publish VroksNet.Web (Blazor WebAssembly, standalone) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS web-build
WORKDIR /src
COPY Directory.Packages.props VroksNet.slnx ./
COPY src/VroksNet.Web/VroksNet.Web.csproj src/VroksNet.Web/
RUN dotnet restore src/VroksNet.Web/VroksNet.Web.csproj
COPY src/VroksNet.Web/ src/VroksNet.Web/
RUN dotnet publish src/VroksNet.Web/VroksNet.Web.csproj -c Release -o /app/web --no-restore

# ---- Stage 2: publish VroksNet.ApiService (+ its Clean Architecture layer dependencies) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
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
RUN dotnet publish src/VroksNet.ApiService/VroksNet.ApiService.csproj -c Release -o /app/api --no-restore

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=api-build /app/api .
COPY --from=web-build /app/web/wwwroot ./wwwroot

# SQLite file lives on a mounted volume so data survives container recreation (see
# docs/project-brief.md section 3 "Хранилище: решение").
VOLUME /app/data
ENV ConnectionStrings__VroksNetDb="Data Source=/app/data/vroksnet.db"

ENTRYPOINT ["dotnet", "VroksNet.ApiService.dll"]
