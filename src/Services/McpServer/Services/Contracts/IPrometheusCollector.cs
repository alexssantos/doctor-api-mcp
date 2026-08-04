namespace McpApis.McpServer.Services.Contracts;

/// <summary>
/// Collects live metrics from Prometheus via its HTTP query API.
/// Used both by the "query_metrics" MCP tool and by the dashboard metrics panel.
/// </summary>
public interface IPrometheusCollector
{
    /// <summary>Executes a PromQL instant query (current value).</summary>
    Task<System.Text.Json.JsonElement> QueryAsync(string promql);

    /// <summary>Executes a PromQL range query between start and end, at the given step (e.g. "15s").</summary>
    Task<System.Text.Json.JsonElement> QueryRangeAsync(string promql, DateTimeOffset start, DateTimeOffset end, string step);

    /// <summary>Returns the current scrape target health (up/down) as reported by Prometheus.</summary>
    Task<System.Text.Json.JsonElement> GetTargetsAsync();
}
