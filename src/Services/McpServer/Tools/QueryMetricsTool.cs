using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class QueryMetricsTool
{
    [McpServerTool(Name = "query_metrics"), Description("Executes a PromQL query against Prometheus to retrieve live metrics such as request rate, error rate, latency, CPU/memory, or target availability. Examples: 'up' to check target health, 'sum(rate(http_server_request_duration_seconds_count{service=\"precoapi\"}[5m]))' for request rate.")]
    public static async Task<string> Execute(
        IPrometheusCollector prometheus,
        IApplicationCatalog catalog,
        [Description("PromQL expression, e.g. 'up' or 'rate(http_server_request_duration_seconds_count[5m])'")] string query)
    {
        // Best-effort gate: refuse queries that name a disabled application.
        // PromQL is free-form (regex matchers, relabels), so this is not
        // hermetic — the limitation is documented in the feature doc.
        var mentioned = catalog.GetAll().FirstOrDefault(a =>
            !a.Enabled && MentionsApp(query, a));
        if (mentioned is not null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Application '{mentioned.Name}' is disabled for MCP indexing; refusing to query its metrics.",
                hint = "Enable it in the dashboard (/dashboard) to query its metrics."
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        var result = await prometheus.QueryAsync(query);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool MentionsApp(string query, DiscoveredApplication app) =>
        new[] { app.Name, app.KubernetesServiceName, app.DeploymentName, app.OtelServiceName }
            .Any(alias => !string.IsNullOrEmpty(alias)
                          && query.Contains(alias, StringComparison.OrdinalIgnoreCase));
}
