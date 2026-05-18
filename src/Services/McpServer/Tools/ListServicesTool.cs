using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class ListServicesTool
{
    [McpServerTool(Name = "list_services"), Description("Lists all Kubernetes services, pods, and deployments in the mcp-apis namespace with their status.")]
    public static async Task<string> Execute(KubernetesService k8s, OpenApiService openApi)
    {
        var services = await k8s.ListServicesAsync();
        var deployments = await k8s.ListDeploymentsAsync();
        var pods = await k8s.ListPodsAsync();
        var apiServices = openApi.GetKnownServices();

        var result = new
        {
            services,
            deployments,
            pods,
            apiServices
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
