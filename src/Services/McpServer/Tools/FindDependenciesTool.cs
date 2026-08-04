using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class FindDependenciesTool
{
    [McpServerTool(Name = "find_dependencies"), Description("Finds service dependencies using Jaeger's dependency graph. Shows which services call which other services.")]
    public static async Task<string> Execute(IJaegerCollector jaeger, IApplicationCatalog catalog)
    {
        var deps = await jaeger.GetDependenciesAsync();

        // Edges touching a disabled application are removed from the graph;
        // the disabled names are listed so the LLM knows data was withheld.
        var disabled = catalog.GetAll()
            .Where(a => !a.Enabled)
            .Select(a => a.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var edges = new List<JsonElement>();
        if (deps.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var edge in data.EnumerateArray())
            {
                var parent = edge.TryGetProperty("parent", out var p) ? p.GetString() : null;
                var child = edge.TryGetProperty("child", out var c) ? c.GetString() : null;

                var touchesDisabled =
                    (parent is not null && !catalog.IsEnabled(parent)) ||
                    (child is not null && !catalog.IsEnabled(child));

                if (!touchesDisabled)
                    edges.Add(edge);
            }
        }

        var result = new
        {
            data = edges,
            disabledApplications = disabled
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
