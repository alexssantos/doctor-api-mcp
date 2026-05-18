using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class ExplainApiTool
{
    [McpServerTool(Name = "explain_api"), Description("Explains what a service API does by combining its OpenAPI spec (routes, methods, descriptions) with recent trace data.")]
    public static async Task<string> Execute(
        IOpenApiCollector openApi,
        IJaegerCollector jaeger,
        [Description("Service name (e.g. precoapi, produtoapi)")] string serviceName)
    {
        var routes = await openApi.GetRoutesAsync(serviceName);
        var services = await jaeger.GetServicesAsync();

        var jaegerServiceName = services.FirstOrDefault(s =>
            s.Equals(serviceName, StringComparison.OrdinalIgnoreCase)) ?? serviceName;

        List<object>? recentTraces = null;
        try
        {
            var spans = await jaeger.GetTraceSpansAsync(jaegerServiceName, limit: 10);
            recentTraces = spans
                .GroupBy(s => s.OperationName)
                .Select(g => (object)new
                {
                    operation = g.Key,
                    callCount = g.Count(),
                    avgDurationMs = g.Average(s => s.Duration) / 1000.0
                })
                .ToList();
        }
        catch
        {
            // Jaeger may not have data yet
        }

        var result = new
        {
            service = serviceName,
            routes = routes.Select(r => new
            {
                r.Method,
                r.Path,
                r.Summary,
                r.OperationId
            }),
            recentActivity = recentTraces
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
