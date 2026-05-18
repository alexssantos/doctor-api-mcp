using System.Net.Http.Json;
using System.Text.Json;

namespace McpApis.McpServer.Services;

public class OpenApiService
{
    private static readonly Dictionary<string, string> ServiceEndpoints = new()
    {
        ["precoapi"] = "http://precoapi",
        ["produtoapi"] = "http://produtoapi"
    };

    private readonly HttpClient _http = new();

    public async Task<string> GetOpenApiSpecAsync(string serviceName)
    {
        if (!ServiceEndpoints.TryGetValue(serviceName.ToLowerInvariant(), out var baseUrl))
            return $"Unknown service: {serviceName}. Available: {string.Join(", ", ServiceEndpoints.Keys)}";

        var response = await _http.GetAsync($"{baseUrl}/openapi/v1.json");
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

    public IReadOnlyCollection<string> GetKnownServices() => ServiceEndpoints.Keys;
}

public class RouteInfo
{
    public string Path { get; set; } = "";
    public string Method { get; set; } = "";
    public string Summary { get; set; } = "";
    public string OperationId { get; set; } = "";
}
