using System.Net.Http.Json;
using System.Text.Json;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

public class PrometheusService : IPrometheusCollector
{
    private readonly HttpClient _http;

    public PrometheusService(HttpClient http)
    {
        _http = http;
    }

    public async Task<JsonElement> QueryAsync(string promql)
    {
        var url = $"/api/v1/query?query={Uri.EscapeDataString(promql)}";
        return await _http.GetFromJsonAsync<JsonElement>(url);
    }

    public async Task<JsonElement> QueryRangeAsync(string promql, DateTimeOffset start, DateTimeOffset end, string step)
    {
        var url = "/api/v1/query_range" +
                  $"?query={Uri.EscapeDataString(promql)}" +
                  $"&start={start.ToUnixTimeSeconds()}" +
                  $"&end={end.ToUnixTimeSeconds()}" +
                  $"&step={Uri.EscapeDataString(step)}";
        return await _http.GetFromJsonAsync<JsonElement>(url);
    }

    public async Task<JsonElement> GetTargetsAsync()
    {
        return await _http.GetFromJsonAsync<JsonElement>("/api/v1/targets");
    }
}
