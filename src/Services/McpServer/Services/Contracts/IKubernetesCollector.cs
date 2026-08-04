namespace McpApis.McpServer.Services.Contracts;

public interface IKubernetesCollector
{
    Task<List<ServiceInfo>> ListServicesAsync();
    Task<List<PodInfo>> ListPodsAsync();
    Task<List<DeploymentInfo>> ListDeploymentsAsync();
    Task<HealthStatus> GetHealthAsync(string appName, string? namespaceName = null);

    /// <summary>
    /// Discovers services in the namespace that carry the given label set to "true".
    /// Returns a map of service-name → base URL (from annotation "mcp-apis/base-url",
    /// or derived as "http://&lt;name&gt;.&lt;namespace&gt;.svc.cluster.local" when absent).
    /// </summary>
    Task<Dictionary<string, string>> DiscoverIndexedServicesAsync(string labelKey);

    /// <summary>Lists every Service in the cluster with selector, labels and annotations.</summary>
    Task<List<ServiceDetail>> ListServiceDetailsAllNamespacesAsync();

    /// <summary>Lists every Deployment in the cluster with its pod template labels.</summary>
    Task<List<DeploymentDetail>> ListDeploymentDetailsAllNamespacesAsync();

    /// <summary>Returns "namespace/serviceName" keys for Services backed by at least one ready endpoint address.</summary>
    Task<HashSet<string>> ListServicesWithReadyEndpointsAsync();

    /// <summary>Reads the data of a ConfigMap in the MCP server's own namespace; null when it does not exist.</summary>
    Task<Dictionary<string, string>?> GetConfigMapDataAsync(string name);

    /// <summary>Replaces the data of a ConfigMap in the MCP server's own namespace (retries on conflict).</summary>
    Task ReplaceConfigMapDataAsync(string name, Dictionary<string, string> data);
}
