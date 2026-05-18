FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy solution + project files first for layer caching
COPY src/mcp-apis.slnx ./src/
COPY src/BuildingBlocks/Observability/McpApis.BuildingBlocks.Observability.csproj ./src/BuildingBlocks/Observability/
COPY src/BuildingBlocks/Http/McpApis.BuildingBlocks.Http.csproj ./src/BuildingBlocks/Http/
COPY src/Services/PrecoAPI/McpApis.PrecoAPI.csproj ./src/Services/PrecoAPI/
COPY src/Services/ProdutoAPI/McpApis.ProdutoAPI.csproj ./src/Services/ProdutoAPI/
COPY src/Services/McpServer/McpApis.McpServer.csproj ./src/Services/McpServer/

# Restore (cached if .csproj unchanged)
RUN dotnet restore src/Services/McpServer/McpApis.McpServer.csproj

# Copy source
COPY src/Services/McpServer/ ./src/Services/McpServer/

# Publish
RUN dotnet publish src/Services/McpServer/McpApis.McpServer.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:4000
EXPOSE 4000

ENTRYPOINT ["dotnet", "McpApis.McpServer.dll"]
