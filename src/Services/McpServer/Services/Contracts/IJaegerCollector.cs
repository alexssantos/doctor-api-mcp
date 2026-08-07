namespace McpApis.McpServer.Services.Contracts;

public interface IJaegerCollector
{
    Task<List<string>> GetServicesAsync(CancellationToken cancellationToken = default);
    Task<System.Text.Json.JsonElement> GetTracesAsync(
        string service,
        int limit = 20,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CancellationToken cancellationToken = default);
    Task<System.Text.Json.JsonElement> GetDependenciesAsync(
        long lookbackMilliseconds = 3_600_000,
        CancellationToken cancellationToken = default);
    Task<List<TraceSpan>> GetTraceSpansAsync(
        string service,
        string? operation = null,
        int limit = 5,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CancellationToken cancellationToken = default);
}
