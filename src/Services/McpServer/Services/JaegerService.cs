using System.Net.Http.Json;
using System.Text.Json;

namespace McpApis.McpServer.Services;

public class JaegerService
{
    private readonly string _baseUrl;
    private readonly HttpClient _http;

    public JaegerService(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public async Task<List<string>> GetServicesAsync()
    {
        var response = await _http.GetFromJsonAsync<JsonElement>("/api/services");
        return response.GetProperty("data").EnumerateArray()
            .Select(s => s.GetString()!)
            .Where(s => s != "jaeger-query")
            .ToList();
    }

    public async Task<JsonElement> GetTracesAsync(string service, int limit = 20)
    {
        var response = await _http.GetFromJsonAsync<JsonElement>(
            $"/api/traces?service={Uri.EscapeDataString(service)}&limit={limit}&lookback=1h");
        return response.GetProperty("data");
    }

    public async Task<JsonElement> GetDependenciesAsync()
    {
        var endTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var response = await _http.GetFromJsonAsync<JsonElement>(
            $"/api/dependencies?endTs={endTs}&lookback=3600000");
        return response;
    }

    public async Task<List<TraceSpan>> GetTraceSpansAsync(string service, string? operation = null, int limit = 5)
    {
        var url = $"/api/traces?service={Uri.EscapeDataString(service)}&limit={limit}&lookback=1h";
        if (operation != null)
            url += $"&operation={Uri.EscapeDataString(operation)}";

        var response = await _http.GetFromJsonAsync<JsonElement>(url);
        var spans = new List<TraceSpan>();

        foreach (var trace in response.GetProperty("data").EnumerateArray())
        {
            foreach (var span in trace.GetProperty("spans").EnumerateArray())
            {
                spans.Add(new TraceSpan
                {
                    TraceId = trace.GetProperty("traceID").GetString()!,
                    SpanId = span.GetProperty("spanID").GetString()!,
                    OperationName = span.GetProperty("operationName").GetString()!,
                    Duration = span.GetProperty("duration").GetInt64(),
                    ServiceName = span.GetProperty("processID").GetString() is string pid
                        ? trace.GetProperty("processes").GetProperty(pid).GetProperty("serviceName").GetString()!
                        : "unknown",
                    Tags = span.TryGetProperty("tags", out var tags)
                        ? tags.EnumerateArray()
                            .ToDictionary(
                                t => t.GetProperty("key").GetString()!,
                                t => t.GetProperty("value").ToString())
                        : new()
                });
            }
        }

        return spans;
    }
}

public class TraceSpan
{
    public string TraceId { get; set; } = "";
    public string SpanId { get; set; } = "";
    public string OperationName { get; set; } = "";
    public long Duration { get; set; }
    public string ServiceName { get; set; } = "";
    public Dictionary<string, string> Tags { get; set; } = new();
}
