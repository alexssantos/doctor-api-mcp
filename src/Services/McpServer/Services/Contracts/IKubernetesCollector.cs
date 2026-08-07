namespace McpApis.McpServer.Services.Contracts;

public interface IKubernetesCollector
{
    Task<List<ServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default);
    Task<List<PodInfo>> ListPodsAsync(CancellationToken cancellationToken = default);
    Task<List<DeploymentInfo>> ListDeploymentsAsync(CancellationToken cancellationToken = default);
    Task<HealthStatus> GetHealthAsync(
        string appName,
        string? namespaceName = null,
        CancellationToken cancellationToken = default);
    Task<WorkloadDetail?> GetWorkloadAsync(
        string namespaceName,
        string? deploymentName,
        IReadOnlyDictionary<string, string> selector,
        CancellationToken cancellationToken = default);
    Task<List<KubernetesEventDetail>> ListEventsAsync(
        string namespaceName,
        DateTimeOffset from,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers services in the namespace that carry the given label set to "true".
    /// Returns a map of service-name → base URL (from annotation "mcp-apis/base-url",
    /// or derived as "http://&lt;name&gt;.&lt;namespace&gt;.svc.cluster.local" when absent).
    /// </summary>
    Task<Dictionary<string, string>> DiscoverIndexedServicesAsync(
        string labelKey,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every Service in the cluster with selector, labels and annotations.</summary>
    Task<List<ServiceDetail>> ListServiceDetailsAllNamespacesAsync(CancellationToken cancellationToken = default);

    Task<List<ServiceDetail>> ListServiceDetailsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every Deployment in the cluster with its pod template labels.</summary>
    Task<List<DeploymentDetail>> ListDeploymentDetailsAllNamespacesAsync(CancellationToken cancellationToken = default);

    Task<List<DeploymentDetail>> ListDeploymentDetailsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default);

    /// <summary>Returns "namespace/serviceName" keys for Services backed by at least one ready endpoint address.</summary>
    Task<HashSet<string>> ListServicesWithReadyEndpointsAsync(CancellationToken cancellationToken = default);

    Task<HashSet<string>> ListServicesWithReadyEndpointsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the data of a ConfigMap in the MCP server's own namespace; null when it does not exist.</summary>
    Task<Dictionary<string, string>?> GetConfigMapDataAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the data of a ConfigMap in the MCP server's own namespace (retries on conflict).</summary>
    Task ReplaceConfigMapDataAsync(
        string name,
        Dictionary<string, string> data,
        CancellationToken cancellationToken = default);
}
