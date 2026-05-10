FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy solution + project files first for layer caching
COPY src/mcp-apis.slnx ./src/
COPY src/BuildingBlocks/Observability/McpApis.BuildingBlocks.Observability.csproj ./src/BuildingBlocks/Observability/
COPY src/BuildingBlocks/Http/McpApis.BuildingBlocks.Http.csproj ./src/BuildingBlocks/Http/
COPY src/Services/PrecoAPI/McpApis.PrecoAPI.csproj ./src/Services/PrecoAPI/
COPY src/Services/ProdutoAPI/McpApis.ProdutoAPI.csproj ./src/Services/ProdutoAPI/

# Restore (cached if .csproj unchanged)
RUN dotnet restore src/Services/ProdutoAPI/McpApis.ProdutoAPI.csproj

# Copy source
COPY src/BuildingBlocks/ ./src/BuildingBlocks/
COPY src/Services/ProdutoAPI/ ./src/Services/ProdutoAPI/

# Publish
RUN dotnet publish src/Services/ProdutoAPI/McpApis.ProdutoAPI.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "McpApis.ProdutoAPI.dll"]
