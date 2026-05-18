using System.Net.Http.Json;
using System.Text.Json;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

public class OpenApiService : IOpenApiCollector
{
    private readonly IServiceRegistry _registry;
    private readonly HttpClient _http = new();

    public OpenApiService(IServiceRegistry registry)
    {
        _registry = registry;
    }

    public async Task<string> GetOpenApiSpecAsync(string serviceName)
    {
        if (!_registry.TryGet(serviceName, out var endpoint))
        {
            var known = string.Join(", ", _registry.GetAll().Keys);
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
