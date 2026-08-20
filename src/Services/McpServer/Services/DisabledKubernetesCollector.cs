using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Used by installations that intentionally receive no Kubernetes API token or
/// RBAC. Providers translate this explicit failure into an unavailable source
/// instead of fabricating empty/healthy Kubernetes data.
/// </summary>
public class DisabledKubernetesCollector : IKubernetesCollector
{
    private const string Message =
        "Kubernetes API access is disabled by ClusterAccess:Scope=None.";

    public virtual Task<bool> CanIAsync(
        string verb,
        string apiGroup,
        string resource,
        string? namespaceName = null,
        string? resourceName = null,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<List<ServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default) =>
        Unavailable<List<ServiceInfo>>();

    public Task<List<PodInfo>> ListPodsAsync(CancellationToken cancellationToken = default) =>
        Unavailable<List<PodInfo>>();

    public Task<List<DeploymentInfo>> ListDeploymentsAsync(CancellationToken cancellationToken = default) =>
        Unavailable<List<DeploymentInfo>>();

    public Task<HealthStatus> GetHealthAsync(
        string appName,
        string? namespaceName = null,
        CancellationToken cancellationToken = default) => Unavailable<HealthStatus>();

    public Task<WorkloadDetail?> GetWorkloadAsync(
        string namespaceName,
        string? deploymentName,
        IReadOnlyDictionary<string, string> selector,
        CancellationToken cancellationToken = default) => Unavailable<WorkloadDetail?>();

    public Task<List<KubernetesEventDetail>> ListEventsAsync(
        string namespaceName,
        DateTimeOffset from,
        CancellationToken cancellationToken = default) => Unavailable<List<KubernetesEventDetail>>();

    public Task<Dictionary<string, string>> DiscoverIndexedServicesAsync(
        string labelKey,
        CancellationToken cancellationToken = default) =>
        Unavailable<Dictionary<string, string>>();

    public Task<List<ServiceDetail>> ListServiceDetailsAllNamespacesAsync(
        CancellationToken cancellationToken = default) => Unavailable<List<ServiceDetail>>();

    public Task<List<ServiceDetail>> ListServiceDetailsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default) => Unavailable<List<ServiceDetail>>();

    public Task<List<DeploymentDetail>> ListDeploymentDetailsAllNamespacesAsync(
        CancellationToken cancellationToken = default) => Unavailable<List<DeploymentDetail>>();

    public Task<List<DeploymentDetail>> ListDeploymentDetailsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default) => Unavailable<List<DeploymentDetail>>();

    public Task<HashSet<string>> ListServicesWithReadyEndpointsAsync(
        CancellationToken cancellationToken = default) => Unavailable<HashSet<string>>();

    public Task<HashSet<string>> ListServicesWithReadyEndpointsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default) => Unavailable<HashSet<string>>();

    public Task<Dictionary<string, string>?> GetConfigMapDataAsync(
        string name,
        CancellationToken cancellationToken = default) =>
        Unavailable<Dictionary<string, string>?>();

    public Task ReplaceConfigMapDataAsync(
        string name,
        Dictionary<string, string> data,
        CancellationToken cancellationToken = default) => Unavailable<object?>();

    private static Task<T> Unavailable<T>() =>
        Task.FromException<T>(new InvalidOperationException(Message));
}
