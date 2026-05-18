namespace McpApis.McpServer.Services.Contracts;

public interface IKubernetesCollector
{
    Task<List<ServiceInfo>> ListServicesAsync();
    Task<List<PodInfo>> ListPodsAsync();
    Task<List<DeploymentInfo>> ListDeploymentsAsync();
    Task<HealthStatus> GetHealthAsync(string appName);

    /// <summary>
    /// Discovers services in the namespace that carry the given label set to "true".
    /// Returns a map of service-name → base URL (from annotation "mcp-apis/base-url",
    /// or derived as "http://&lt;name&gt;" when absent).
    /// </summary>
    Task<Dictionary<string, string>> DiscoverIndexedServicesAsync(string labelKey);
}
