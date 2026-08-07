namespace McpApis.McpServer.Services.Contracts;

/// <summary>
/// Collects live metrics from Prometheus via its HTTP query API.
/// Low-level collector used by the feature-gated raw administrative surface and
/// by normalized providers whose query descriptors are controlled server-side.
/// </summary>
public interface IPrometheusCollector
{
    /// <summary>Executes a PromQL instant query (current value).</summary>
    Task<System.Text.Json.JsonElement> QueryAsync(
        string promql,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a PromQL range query between start and end, at the given step (e.g. "15s").</summary>
    Task<System.Text.Json.JsonElement> QueryRangeAsync(
        string promql,
        DateTimeOffset start,
        DateTimeOffset end,
        string step,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current scrape target health (up/down) as reported by Prometheus.</summary>
    Task<System.Text.Json.JsonElement> GetTargetsAsync(CancellationToken cancellationToken = default);
}
