namespace McpApis.McpServer.Services.Contracts;

public interface IJaegerCollector
{
    Task<List<string>> GetServicesAsync();
    Task<System.Text.Json.JsonElement> GetTracesAsync(string service, int limit = 20);
    Task<System.Text.Json.JsonElement> GetDependenciesAsync();
    Task<List<TraceSpan>> GetTraceSpansAsync(string service, string? operation = null, int limit = 5);
}
