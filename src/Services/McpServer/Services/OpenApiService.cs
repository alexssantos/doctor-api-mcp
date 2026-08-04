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

    public async Task<string> GetOpenApiSpecAsync(string serviceName)
    {
        if (!_registry.TryGet(serviceName, out var endpoint))
        {
            var known = string.Join(", ", _registry.GetAll().Keys);

            // Distinguish "disabled by the operator" and "discovered but not
            // indexable" from a truly unknown name so the LLM gets an actionable hint.
            if (_catalog.TryGet(serviceName, out var app))
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

        var response = await _http.GetAsync($"{endpoint.BaseUrl}{endpoint.OpenApiPath}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<List<RouteInfo>> GetRoutesAsync(string serviceName)
    {
        var spec = await GetOpenApiSpecAsync(serviceName);
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

                    routes.Add(new RouteInfo
                    {
                        Path = path.Name,
                        Method = method.Name.ToUpper(),
                        Summary = summary,
                        OperationId = operationId
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
}
