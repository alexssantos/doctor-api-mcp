using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class FindDataOriginTool
{
    [McpServerTool(Name = "find_data_origin"), Description("Traces the origin of data for a given route by combining OpenAPI structure with Jaeger traces. Shows the full call chain from client to database.")]
    public static async Task<string> Execute(
        OpenApiService openApi,
        JaegerService jaeger,
        KubernetesService k8s,
        [Description("Service name (e.g. produtoapi)")] string serviceName,
        [Description("Route path (e.g. /api/produtos/{id})")] string route)
    {
        // Get routes for context
        var routes = await openApi.GetRoutesAsync(serviceName);
        var matchingRoute = routes.FirstOrDefault(r =>
            r.Path.Equals(route, StringComparison.OrdinalIgnoreCase));

        // Get Jaeger services list
        var jaegerServices = await jaeger.GetServicesAsync();
        var jaegerServiceName = jaegerServices.FirstOrDefault(s =>
            s.Equals(serviceName, StringComparison.OrdinalIgnoreCase)) ?? serviceName;

        // Get traces for this route
        var spans = await jaeger.GetTraceSpansAsync(jaegerServiceName, limit: 10);

        // Filter spans related to this route
        var routeSpans = spans.Where(s =>
            s.Tags.GetValueOrDefault("http.route", "")
                .Equals(route, StringComparison.OrdinalIgnoreCase) ||
            s.OperationName.Contains(route, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // If no route-specific spans, use all
        if (routeSpans.Count == 0)
            routeSpans = spans;

        // Get related trace IDs and build call chain
        var traceIds = routeSpans.Select(s => s.TraceId).Distinct().Take(3).ToList();
        var allRelatedSpans = spans.Where(s => traceIds.Contains(s.TraceId)).ToList();

        // Build data flow
        var dataFlow = allRelatedSpans
            .GroupBy(s => s.TraceId)
            .Select(g => new
            {
                traceId = g.Key,
                chain = g.OrderBy(s => s.Duration).Select(s => new
                {
                    s.ServiceName,
                    s.OperationName,
                    durationMs = s.Duration / 1000.0,
                    dbStatement = s.Tags.GetValueOrDefault("db.statement", ""),
                    dbSystem = s.Tags.GetValueOrDefault("db.system", ""),
                    httpRoute = s.Tags.GetValueOrDefault("http.route", ""),
                    httpMethod = s.Tags.GetValueOrDefault("http.request.method", ""),
                    peerService = s.Tags.GetValueOrDefault("peer.service", "")
                })
            });

        // Get pod info for context
        var pods = await k8s.ListPodsAsync();
        var relevantPods = pods.Where(p =>
            p.App.Equals(serviceName, StringComparison.OrdinalIgnoreCase)).ToList();

        var result = new
        {
            service = serviceName,
            route,
            matchingEndpoint = matchingRoute != null ? new { matchingRoute.Method, matchingRoute.Path, matchingRoute.Summary } : null,
            dataFlow,
            runningPods = relevantPods.Select(p => new { p.Name, p.Status, p.Ready })
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
