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
        IApplicationCatalog catalog,
        [Description("App label to check (e.g. catalog-api, orders-api, jaeger, prometheus, grafana)")] string appName)
    {
        if (!ToolGuard.EnsureEnabled(catalog, appName, out var error))
            return error;

        // Use the discovered app's own label/namespace when known so lookups work
        // for applications living outside the MCP server's namespace.
        string? ns = null;
        if (catalog.TryGet(appName, out var app))
        {
            appName = app.DeploymentName ?? app.KubernetesServiceName ?? appName;
            ns = app.Namespace;
        }

        var health = await k8s.GetHealthAsync(appName, ns);

        return JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true });
    }
}
