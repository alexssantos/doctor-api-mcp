using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class GetHealthTool
{
    [McpServerTool(Name = "get_health"), Description("Checks the health of a service by inspecting its Kubernetes pods (ready state, restarts, container status).")]
    public static async Task<string> Execute(
        IKubernetesCollector k8s,
        [Description("App label to check (e.g. precoapi, produtoapi, jaeger, prometheus, grafana)")] string appName)
    {
        var health = await k8s.GetHealthAsync(appName);

        return JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true });
    }
}
