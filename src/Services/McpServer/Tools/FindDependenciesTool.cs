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
    public static async Task<string> Execute(IJaegerCollector jaeger)
    {
        var deps = await jaeger.GetDependenciesAsync();

        return JsonSerializer.Serialize(deps, new JsonSerializerOptions { WriteIndented = true });
    }
}
