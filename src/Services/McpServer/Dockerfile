FROM --platform=$BUILDPLATFORM node:22-alpine AS dashboard-build
WORKDIR /dashboard
COPY src/Services/McpServer/dashboard/package*.json ./
RUN npm ci
COPY src/Services/McpServer/dashboard/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /repo

# Copy solution + project files first for layer caching
COPY src/mcp-apis.slnx ./src/
COPY src/BuildingBlocks/Observability/McpApis.BuildingBlocks.Observability.csproj ./src/BuildingBlocks/Observability/
COPY src/BuildingBlocks/Http/McpApis.BuildingBlocks.Http.csproj ./src/BuildingBlocks/Http/
COPY src/Services/PrecoAPI/McpApis.PrecoAPI.csproj ./src/Services/PrecoAPI/
COPY src/Services/ProdutoAPI/McpApis.ProdutoAPI.csproj ./src/Services/ProdutoAPI/
COPY src/Services/McpServer/McpApis.McpServer.csproj ./src/Services/McpServer/

# Restore (cached if .csproj unchanged)
RUN dotnet restore src/Services/McpServer/McpApis.McpServer.csproj -a "$TARGETARCH"

# Copy source
COPY src/BuildingBlocks/ ./src/BuildingBlocks/
COPY src/Services/McpServer/ ./src/Services/McpServer/

# Bring in the pre-built React dashboard (Vite outDir already points at wwwroot/dashboard,
# but the dashboard/ source isn't compiled by MSBuild, so we overlay the built assets here)
COPY --from=dashboard-build /wwwroot/dashboard ./src/Services/McpServer/wwwroot/dashboard

# Publish
RUN dotnet publish src/Services/McpServer/McpApis.McpServer.csproj \
    -c Release \
    -o /app/publish \
    -a "$TARGETARCH" \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ARG VERSION=dev
ARG REVISION=unknown
LABEL org.opencontainers.image.title="doctor-api-mcp" \
      org.opencontainers.image.description="MCP server for Kubernetes observability and explainable incident analysis" \
      org.opencontainers.image.url="https://github.com/alexssantos/doctor-api-mcp" \
      org.opencontainers.image.source="https://github.com/alexssantos/doctor-api-mcp" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.licenses="MIT"

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:4000
EXPOSE 4000

# Official .NET Linux images provide the non-root user through APP_UID.
USER $APP_UID

ENTRYPOINT ["dotnet", "McpApis.McpServer.dll"]

