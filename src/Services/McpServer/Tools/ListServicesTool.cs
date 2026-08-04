using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class ListServicesTool
{
    [McpServerTool(Name = "list_services"), Description("Lists all Kubernetes services, pods, and deployments in the mcp-apis namespace with their status.")]
    public static async Task<string> Execute(
        IKubernetesCollector k8s,
        IOpenApiCollector openApi,
        IApplicationCatalog catalog)
    {
        var services = await k8s.ListServicesAsync();
        var deployments = await k8s.ListDeploymentsAsync();
        var pods = await k8s.ListPodsAsync();
        var apiServices = openApi.GetKnownServices();

        // Kubernetes objects belonging to disabled applications are omitted; the
        // disabled names are surfaced so the LLM knows they exist but are hidden.
        var disabled = catalog.GetAll()
            .Where(a => !a.Enabled)
            .Select(a => a.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new
        {
            services = services.Where(s => catalog.IsEnabled(s.Name)),
            deployments = deployments.Where(d => catalog.IsEnabled(d.Name)),
            pods = pods.Where(p => catalog.IsEnabled(p.App)),
            apiServices,
            disabledApplications = disabled
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
