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
        [Description("PromQL expression, e.g. 'up' or 'rate(http_server_request_duration_seconds_count[5m])'")] string query)
    {
        var result = await prometheus.QueryAsync(query);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
