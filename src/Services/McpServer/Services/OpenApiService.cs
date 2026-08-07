using System.Net.Http.Json;
using System.Text.Json;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

public class OpenApiService : IOpenApiCollector
{
    private readonly IServiceRegistry _registry;
    private readonly IApplicationCatalog _catalog;
    private readonly HttpClient _http;

    public OpenApiService(IServiceRegistry registry, IApplicationCatalog catalog, HttpClient http)
    {
        _registry = registry;
        _catalog = catalog;
        _http = http;
    }

    public async Task<string> GetOpenApiSpecAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        return await GetOpenApiSpecCoreAsync(serviceName, null, cancellationToken);
    }

    public async Task<string> GetOpenApiSpecAsync(
        string serviceName,
        string namespaceName,
        CancellationToken cancellationToken = default) =>
        await GetOpenApiSpecCoreAsync(serviceName, namespaceName, cancellationToken);

    private async Task<string> GetOpenApiSpecCoreAsync(
        string serviceName,
        string? namespaceName,
        CancellationToken cancellationToken)
    {
        ServiceEndpoint? endpoint = null;
        if (namespaceName is not null &&
            _catalog.TryGet(serviceName, namespaceName, out var namespacedApp) &&
            namespacedApp.Enabled && namespacedApp.OpenApi.Validated &&
            namespacedApp.BaseUrl is not null && namespacedApp.OpenApi.Path is not null)
        {
            endpoint = new ServiceEndpoint(namespacedApp.BaseUrl, namespacedApp.OpenApi.Path);
        }
        else if (namespaceName is null && _registry.TryGet(serviceName, out var legacyEndpoint))
        {
            endpoint = legacyEndpoint;
        }

        if (endpoint is null)
        {
            var known = string.Join(", ", _registry.GetAll().Keys);

            // Distinguish "disabled by the operator" and "discovered but not
            // indexable" from a truly unknown name so the LLM gets an actionable hint.
            DiscoveredApplication app;
            var found = namespaceName is null
                ? _catalog.TryGet(serviceName, out app)
                : _catalog.TryGet(serviceName, namespaceName, out app);
            if (found)
            {
                if (!app.Enabled)
                    return $"Service '{app.Name}' is disabled for MCP indexing. " +
                           $"Enable it in the dashboard (/dashboard). Available: {known}";

                if (!app.OpenApi.Validated)
                    return $"Service '{app.Name}' has no valid OpenAPI spec: " +
                           $"{string.Join("; ", app.OpenApi.Failures)}. Available: {known}";
            }

            return $"Unknown service: {serviceName}. Available: {known}";
        }

        using var response = await _http.GetAsync(
            $"{endpoint.BaseUrl}{endpoint.OpenApiPath}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<List<RouteInfo>> GetRoutesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        return await GetRoutesCoreAsync(serviceName, null, cancellationToken);
    }

    public async Task<List<RouteInfo>> GetRoutesAsync(
        string serviceName,
        string namespaceName,
        CancellationToken cancellationToken = default) =>
        await GetRoutesCoreAsync(serviceName, namespaceName, cancellationToken);

    private async Task<List<RouteInfo>> GetRoutesCoreAsync(
        string serviceName,
        string? namespaceName,
        CancellationToken cancellationToken)
    {
        var spec = namespaceName is null
            ? await GetOpenApiSpecAsync(serviceName, cancellationToken)
            : await GetOpenApiSpecAsync(serviceName, namespaceName, cancellationToken);
        var doc = JsonDocument.Parse(spec);
        var routes = new List<RouteInfo>();

        if (doc.RootElement.TryGetProperty("paths", out var paths))
        {
            foreach (var path in paths.EnumerateObject())
            {
                foreach (var method in path.Value.EnumerateObject())
                {
                    var summary = method.Value.TryGetProperty("summary", out var s)
                        ? s.GetString() ?? ""
                        : "";
                    var operationId = method.Value.TryGetProperty("operationId", out var op)
                        ? op.GetString() ?? ""
                        : "";
                    var description = method.Value.TryGetProperty("description", out var desc)
                        ? desc.GetString() ?? ""
                        : "";
                    var responseCodes = method.Value.TryGetProperty("responses", out var responses)
                        ? responses.EnumerateObject().Select(r => r.Name).ToList()
                        : [];

                    routes.Add(new RouteInfo
                    {
                        Path = path.Name,
                        Method = method.Name.ToUpper(),
                        Summary = summary,
                        OperationId = operationId,
                        Description = description,
                        ResponseCodes = responseCodes
                    });
                }
            }
        }

        return routes;
    }

    public IReadOnlyCollection<string> GetKnownServices() => [.. _registry.GetAll().Keys];
}

public class RouteInfo
{
    public string Path { get; set; } = "";
    public string Method { get; set; } = "";
    public string Summary { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> ResponseCodes { get; set; } = [];
}
