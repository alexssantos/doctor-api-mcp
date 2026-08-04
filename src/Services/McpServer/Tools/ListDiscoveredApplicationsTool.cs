using System.ComponentModel;
using System.Text.Json;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools;

[McpServerToolType]
public class ListDiscoveredApplicationsTool
{
    [McpServerTool(Name = "list_discovered_applications"),
     Description("Lists every application auto-discovered in the cluster via deployments, network (services/endpoints) and OpenTelemetry traces — including disabled ones and why an app may not be indexable.")]
    public static string Execute(IApplicationCatalog catalog)
    {
        var apps = catalog.GetAll().Select(a => new
        {
            name = a.Name,
            ns = a.Namespace,
            sources = DescribeSources(a.Sources),
            deploymentName = a.DeploymentName,
            kubernetesServiceName = a.KubernetesServiceName,
            otelServiceName = a.OtelServiceName,
            baseUrl = a.BaseUrl,
            hasReadyEndpoints = a.HasReadyEndpoints,
            openApi = new
            {
                validated = a.OpenApi.Validated,
                path = a.OpenApi.Path,
                failures = a.OpenApi.Failures
            },
            enabled = a.Enabled,
            lockedDisabled = a.LockedDisabled,
            firstSeen = a.FirstSeen,
            lastSeen = a.LastSeen
        });

        return JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static List<string> DescribeSources(DiscoverySources sources)
    {
        var result = new List<string>();
        if (sources.HasFlag(DiscoverySources.Deployment)) result.Add("deployment");
        if (sources.HasFlag(DiscoverySources.Network)) result.Add("network");
        if (sources.HasFlag(DiscoverySources.OpenTelemetry)) result.Add("otel");
        if (sources.HasFlag(DiscoverySources.Config)) result.Add("config");
        return result;
    }
}
