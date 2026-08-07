using System.Net.Http.Json;
using System.Text.Json;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

public class JaegerService : IJaegerCollector
{
    private readonly HttpClient _http;

    public JaegerService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<string>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetFromJsonAsync<JsonElement>("/api/services", cancellationToken);
        return response.GetProperty("data").EnumerateArray()
            .Select(s => s.GetString()!)
            .Where(s => s != "jaeger-query")
            .ToList();
    }

    public async Task<JsonElement> GetTracesAsync(
        string service,
        int limit = 20,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.GetFromJsonAsync<JsonElement>(
            BuildTraceUrl(service, null, limit, start, end), cancellationToken);
        return response.GetProperty("data");
    }

    public async Task<JsonElement> GetDependenciesAsync(
        long lookbackMilliseconds = 3_600_000,
        CancellationToken cancellationToken = default)
    {
        var endTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var response = await _http.GetFromJsonAsync<JsonElement>(
            $"/api/dependencies?endTs={endTs}&lookback={Math.Max(1, lookbackMilliseconds)}",
            cancellationToken);
        return response;
    }

    public async Task<List<TraceSpan>> GetTraceSpansAsync(
        string service,
        string? operation = null,
        int limit = 5,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.GetFromJsonAsync<JsonElement>(
            BuildTraceUrl(service, operation, limit, start, end), cancellationToken);
        var spans = new List<TraceSpan>();

        foreach (var trace in response.GetProperty("data").EnumerateArray())
        {
            foreach (var span in trace.GetProperty("spans").EnumerateArray())
            {
                var parsedTags = span.TryGetProperty("tags", out var tags)
                    ? tags.EnumerateArray()
                        .Where(t => t.TryGetProperty("key", out _))
                        .GroupBy(t => t.GetProperty("key").GetString() ?? string.Empty)
                        .Where(g => g.Key.Length > 0)
                        .ToDictionary(g => g.Key, g => g.Last().GetProperty("value").ToString())
                    : new Dictionary<string, string>();

                var parentSpanId = span.TryGetProperty("references", out var references)
                    ? references.EnumerateArray()
                        .Where(r => r.TryGetProperty("refType", out var type) &&
                                    type.GetString() == "CHILD_OF")
                        .Select(r => r.TryGetProperty("spanID", out var parent) ? parent.GetString() : null)
                        .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                    : null;

                var events = new List<string>();
                if (span.TryGetProperty("logs", out var logs) && logs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var log in logs.EnumerateArray())
                    {
                        if (!log.TryGetProperty("fields", out var fields))
                            continue;
                        var values = fields.EnumerateArray()
                            .Select(f => $"{f.GetProperty("key").GetString()}={f.GetProperty("value")}");
                        events.Add(string.Join(", ", values));
                    }
                }

                var startMicros = span.TryGetProperty("startTime", out var started)
                    ? started.GetInt64()
                    : 0;
                var status = parsedTags.GetValueOrDefault("otel.status_code") ??
                             parsedTags.GetValueOrDefault("status.code") ??
                             (parsedTags.GetValueOrDefault("error") == "true" ? "ERROR" : "UNSET");

                spans.Add(new TraceSpan
                {
                    TraceId = trace.GetProperty("traceID").GetString()!,
                    SpanId = span.GetProperty("spanID").GetString()!,
                    OperationName = span.GetProperty("operationName").GetString()!,
                    Duration = span.GetProperty("duration").GetInt64(),
                    StartedAt = startMicros > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(startMicros / 1000)
                        : DateTimeOffset.UnixEpoch,
                    ParentSpanId = parentSpanId,
                    Status = status,
                    ServiceName = span.GetProperty("processID").GetString() is string pid
                        ? trace.GetProperty("processes").GetProperty(pid).GetProperty("serviceName").GetString()!
                        : "unknown",
                    Tags = parsedTags,
                    Events = events
                });
            }
        }

        return spans;
    }

    private static string BuildTraceUrl(
        string service,
        string? operation,
        int limit,
        DateTimeOffset? start,
        DateTimeOffset? end)
    {
        var url = $"/api/traces?service={Uri.EscapeDataString(service)}&limit={Math.Max(1, limit)}";
        if (operation is not null)
            url += $"&operation={Uri.EscapeDataString(operation)}";
        if (start is not null && end is not null)
        {
            url += $"&start={start.Value.ToUnixTimeMilliseconds() * 1000}" +
                   $"&end={end.Value.ToUnixTimeMilliseconds() * 1000}";
        }
        else
        {
            url += "&lookback=1h";
        }
        return url;
    }
}

public class TraceSpan
{
    public string TraceId { get; set; } = "";
    public string SpanId { get; set; } = "";
    public string OperationName { get; set; } = "";
    public long Duration { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string? ParentSpanId { get; set; }
    public string Status { get; set; } = "UNSET";
    public string ServiceName { get; set; } = "";
    public Dictionary<string, string> Tags { get; set; } = new();
    public List<string> Events { get; set; } = [];
}
