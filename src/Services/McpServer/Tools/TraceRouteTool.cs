using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class TraceRouteTool
{
    [McpServerTool(Name = "trace_route"), Description("Retrieves recent traces for a service and optional operation/route from Jaeger. Shows the call chain and timing.")]
    public static async Task<string> Execute(
        IJaegerCollector jaeger,
        [Description("Service name to trace (e.g. PrecoAPI, ProdutoAPI)")] string service,
        [Description("Optional operation name or HTTP route to filter (e.g. GET /api/precos)")] string? operation = null,
        [Description("Max number of traces to return (default 5)")] int limit = 5)
    {
        var spans = await jaeger.GetTraceSpansAsync(service, operation, limit);

        var grouped = spans
            .GroupBy(s => s.TraceId)
            .Select(g => new
            {
                traceId = g.Key,
                spans = g.Select(s => new
                {
                    s.OperationName,
                    s.ServiceName,
                    durationMs = s.Duration / 1000.0,
                    httpMethod = s.Tags.GetValueOrDefault("http.request.method", ""),
                    httpRoute = s.Tags.GetValueOrDefault("http.route", ""),
                    httpStatus = s.Tags.GetValueOrDefault("http.response.status_code", "")
                })
            });

        return JsonSerializer.Serialize(grouped, new JsonSerializerOptions { WriteIndented = true });
    }
}
