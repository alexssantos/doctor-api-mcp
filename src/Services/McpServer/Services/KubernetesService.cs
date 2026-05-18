using k8s;
using k8s.Models;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

public class KubernetesService : IKubernetesCollector
{
    private readonly string _namespace;
    private readonly Kubernetes _client;

    public KubernetesService(string namespaceName)
    {
        _namespace = namespaceName;
        var config = KubernetesClientConfiguration.InClusterConfig();
        _client = new Kubernetes(config);
    }

    public async Task<List<ServiceInfo>> ListServicesAsync()
    {
        var services = await _client.ListNamespacedServiceAsync(_namespace);
        return services.Items.Select(s => new ServiceInfo
        {
            Name = s.Metadata.Name,
            Type = s.Spec.Type,
            ClusterIP = s.Spec.ClusterIP,
            Ports = s.Spec.Ports?.Select(p => $"{p.Port}/{p.Protocol}").ToList() ?? []
        }).ToList();
    }

    public async Task<List<PodInfo>> ListPodsAsync()
    {
        var pods = await _client.ListNamespacedPodAsync(_namespace);
        return pods.Items.Select(p => new PodInfo
        {
            Name = p.Metadata.Name,
            Status = p.Status.Phase,
            Ready = p.Status.ContainerStatuses?.All(c => c.Ready) ?? false,
            Restarts = p.Status.ContainerStatuses?.Sum(c => c.RestartCount) ?? 0,
            App = p.Metadata.Labels != null && p.Metadata.Labels.TryGetValue("app", out var app) ? app : "unknown"
        }).ToList();
    }

    public async Task<List<DeploymentInfo>> ListDeploymentsAsync()
    {
        var deployments = await _client.ListNamespacedDeploymentAsync(_namespace);
        return deployments.Items.Select(d => new DeploymentInfo
        {
            Name = d.Metadata.Name,
            Replicas = d.Spec.Replicas ?? 0,
            ReadyReplicas = d.Status.ReadyReplicas ?? 0,
            Available = d.Status.AvailableReplicas ?? 0
        }).ToList();
    }

    public async Task<HealthStatus> GetHealthAsync(string appName)
    {
        var pods = await _client.ListNamespacedPodAsync(
            _namespace,
            labelSelector: $"app={appName}");

        return new HealthStatus
        {
            Service = appName,
            PodCount = pods.Items.Count,
            AllReady = pods.Items.All(p => p.Status.ContainerStatuses?.All(c => c.Ready) ?? false),
            Pods = pods.Items.Select(p => new PodHealth
            {
                Name = p.Metadata.Name,
                Phase = p.Status.Phase,
                Ready = p.Status.ContainerStatuses?.All(c => c.Ready) ?? false,
                Restarts = p.Status.ContainerStatuses?.Sum(c => c.RestartCount) ?? 0,
                ContainerStates = p.Status.ContainerStatuses?.Select(c =>
                {
                    if (c.State.Running != null) return "Running";
                    if (c.State.Waiting != null) return $"Waiting: {c.State.Waiting.Reason}";
                    if (c.State.Terminated != null) return $"Terminated: {c.State.Terminated.Reason}";
                    return "Unknown";
                }).ToList() ?? []
            }).ToList()
        };
    }

    public async Task<Dictionary<string, string>> DiscoverIndexedServicesAsync(string labelKey)
    {
        var services = await _client.ListNamespacedServiceAsync(
            _namespace,
            labelSelector: $"{labelKey}=true");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var svc in services.Items)
        {
            var name = svc.Metadata.Name;
            var ns = svc.Metadata.NamespaceProperty ?? _namespace;
            // Prefer explicit annotation; fall back to FQDN so the URL is always
            // resolvable from any namespace within the cluster.
            var baseUrl = svc.Metadata.Annotations != null
                && svc.Metadata.Annotations.TryGetValue("mcp-apis/base-url", out var url)
                ? url
                : $"http://{name}.{ns}.svc.cluster.local";
            result[name] = baseUrl;
        }
        return result;
    }
}

public class ServiceInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string ClusterIP { get; set; } = "";
    public List<string> Ports { get; set; } = [];
}

public class PodInfo
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Ready { get; set; }
    public int Restarts { get; set; }
    public string App { get; set; } = "";
}

public class DeploymentInfo
{
    public string Name { get; set; } = "";
    public int Replicas { get; set; }
    public int ReadyReplicas { get; set; }
    public int Available { get; set; }
}

public class HealthStatus
{
    public string Service { get; set; } = "";
    public int PodCount { get; set; }
    public bool AllReady { get; set; }
    public List<PodHealth> Pods { get; set; } = [];
}

public class PodHealth
{
    public string Name { get; set; } = "";
    public string Phase { get; set; } = "";
    public bool Ready { get; set; }
    public int Restarts { get; set; }
    public List<string> ContainerStates { get; set; } = [];
}
