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

    public async Task<bool> CanIAsync(
        string verb,
        string apiGroup,
        string resource,
        string? namespaceName = null,
        string? resourceName = null,
        CancellationToken cancellationToken = default)
    {
        var review = new V1SelfSubjectAccessReview
        {
            Spec = new V1SelfSubjectAccessReviewSpec
            {
                ResourceAttributes = new V1ResourceAttributes
                {
                    Verb = verb,
                    Group = apiGroup,
                    Resource = resource,
                    NamespaceProperty = namespaceName,
                    Name = resourceName
                }
            }
        };
        var response = await _client.AuthorizationV1.CreateSelfSubjectAccessReviewAsync(
            review, cancellationToken: cancellationToken);
        return response.Status?.Allowed ?? false;
    }

    public async Task<List<ServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = await _client.ListNamespacedServiceAsync(
            _namespace, cancellationToken: cancellationToken);
        return services.Items.Select(s => new ServiceInfo
        {
            Name = s.Metadata.Name,
            Type = s.Spec.Type,
            ClusterIP = s.Spec.ClusterIP,
            Ports = s.Spec.Ports?.Select(p => $"{p.Port}/{p.Protocol}").ToList() ?? []
        }).ToList();
    }

    public async Task<List<PodInfo>> ListPodsAsync(CancellationToken cancellationToken = default)
    {
        var pods = await _client.ListNamespacedPodAsync(
            _namespace, cancellationToken: cancellationToken);
        return pods.Items.Select(p => new PodInfo
        {
            Name = p.Metadata.Name,
            Status = p.Status.Phase,
            Ready = p.Status.ContainerStatuses?.All(c => c.Ready) ?? false,
            Restarts = p.Status.ContainerStatuses?.Sum(c => c.RestartCount) ?? 0,
            App = p.Metadata.Labels != null && p.Metadata.Labels.TryGetValue("app", out var app) ? app : "unknown"
        }).ToList();
    }

    public async Task<List<DeploymentInfo>> ListDeploymentsAsync(CancellationToken cancellationToken = default)
    {
        var deployments = await _client.ListNamespacedDeploymentAsync(
            _namespace, cancellationToken: cancellationToken);
        return deployments.Items.Select(d => new DeploymentInfo
        {
            Name = d.Metadata.Name,
            Replicas = d.Spec.Replicas ?? 0,
            ReadyReplicas = d.Status.ReadyReplicas ?? 0,
            Available = d.Status.AvailableReplicas ?? 0
        }).ToList();
    }

    public async Task<HealthStatus> GetHealthAsync(
        string appName,
        string? namespaceName = null,
        CancellationToken cancellationToken = default)
    {
        if (appName.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new ArgumentException("Application name contains invalid label-selector characters.", nameof(appName));

        var pods = await _client.ListNamespacedPodAsync(
            namespaceName ?? _namespace,
            labelSelector: $"app={appName}",
            cancellationToken: cancellationToken);

        return new HealthStatus
        {
            Service = appName,
            PodCount = pods.Items.Count,
            AllReady = pods.Items.Count > 0 &&
                       pods.Items.All(p => p.Status.ContainerStatuses?.All(c => c.Ready) ?? false),
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

    public async Task<WorkloadDetail?> GetWorkloadAsync(
        string namespaceName,
        string? deploymentName,
        IReadOnlyDictionary<string, string> selector,
        CancellationToken cancellationToken = default)
    {
        V1Deployment? deployment = null;
        if (!string.IsNullOrWhiteSpace(deploymentName))
        {
            try
            {
                deployment = await _client.ReadNamespacedDeploymentAsync(
                    deploymentName, namespaceName, cancellationToken: cancellationToken);
            }
            catch (k8s.Autorest.HttpOperationException ex)
                when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // A service can exist without a Deployment (or the workload was just removed).
            }
        }

        var effectiveSelector = selector.Count > 0
            ? new Dictionary<string, string>(selector)
            : deployment?.Spec.Selector?.MatchLabels is { Count: > 0 } labels
                ? new Dictionary<string, string>(labels)
                : [];

        List<V1Pod> podItems;
        if (effectiveSelector.Count == 0)
        {
            podItems = [];
        }
        else
        {
            var podList = await _client.ListNamespacedPodAsync(
                namespaceName,
                labelSelector: BuildLabelSelector(effectiveSelector),
                cancellationToken: cancellationToken);
            podItems = podList.Items.ToList();
        }

        if (deployment is null && podItems.Count == 0)
            return null;

        var podDetails = podItems.Select(ToPodDetail).ToList();
        var image = deployment?.Spec.Template.Spec?.Containers?.FirstOrDefault()?.Image;
        var imageDigest = podItems
            .SelectMany(p => p.Status.ContainerStatuses ?? [])
            .Select(c => c.ImageID)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        return new WorkloadDetail
        {
            DeploymentName = deployment?.Metadata.Name ?? deploymentName ?? "unknown",
            Namespace = namespaceName,
            DesiredReplicas = deployment?.Spec.Replicas ?? podItems.Count,
            ReadyReplicas = deployment?.Status.ReadyReplicas ?? podDetails.Count(p => p.Ready),
            AvailableReplicas = deployment?.Status.AvailableReplicas ?? podDetails.Count(p => p.Ready),
            Revision = GetDictionaryValue(
                deployment?.Metadata.Annotations, "deployment.kubernetes.io/revision"),
            Image = image,
            ImageDigest = imageDigest,
            Selector = effectiveSelector,
            Pods = podDetails
        };
    }

    public async Task<List<KubernetesEventDetail>> ListEventsAsync(
        string namespaceName,
        DateTimeOffset from,
        CancellationToken cancellationToken = default)
    {
        var events = await CoreV1OperationsExtensions.ListNamespacedEventAsync(
            (ICoreV1Operations)_client,
            namespaceName,
            cancellationToken: cancellationToken);

        return events.Items
            .Select(e =>
            {
                var timestamp = e.EventTime ?? e.LastTimestamp ?? e.FirstTimestamp ??
                                e.Metadata.CreationTimestamp ?? DateTime.UtcNow;
                return new KubernetesEventDetail
                {
                    Id = e.Metadata.Uid ?? $"{namespaceName}/{e.Metadata.Name}",
                    Timestamp = new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
                    Type = e.Type ?? "Normal",
                    Reason = e.Reason ?? "Unknown",
                    Message = e.Message ?? string.Empty,
                    InvolvedKind = e.InvolvedObject?.Kind ?? "Unknown",
                    InvolvedName = e.InvolvedObject?.Name ?? "Unknown",
                    Count = e.Count ?? e.Series?.Count ?? 1
                };
            })
            .Where(e => e.Timestamp >= from)
            .OrderBy(e => e.Timestamp)
            .ToList();
    }

    public async Task<Dictionary<string, string>> DiscoverIndexedServicesAsync(
        string labelKey,
        CancellationToken cancellationToken = default)
    {
        var services = await _client.ListNamespacedServiceAsync(
            _namespace,
            labelSelector: $"{labelKey}=true",
            cancellationToken: cancellationToken);

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

    public async Task<List<ServiceDetail>> ListServiceDetailsAllNamespacesAsync(
        CancellationToken cancellationToken = default)
    {
        var services = await _client.ListServiceForAllNamespacesAsync(
            cancellationToken: cancellationToken);
        return services.Items.Select(ToServiceDetail).ToList();
    }

    public async Task<List<ServiceDetail>> ListServiceDetailsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ServiceDetail>();
        foreach (var namespaceName in namespaces.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var services = await _client.ListNamespacedServiceAsync(
                namespaceName, cancellationToken: cancellationToken);
            result.AddRange(services.Items.Select(ToServiceDetail));
        }
        return result;
    }

    private static ServiceDetail ToServiceDetail(V1Service s) => new()
        {
            Name = s.Metadata.Name,
            Namespace = s.Metadata.NamespaceProperty ?? "",
            Selector = s.Spec.Selector is null
                ? []
                : new Dictionary<string, string>(s.Spec.Selector),
            Labels = s.Metadata.Labels is null
                ? []
                : new Dictionary<string, string>(s.Metadata.Labels),
            Annotations = s.Metadata.Annotations is null
                ? []
                : new Dictionary<string, string>(s.Metadata.Annotations),
            Ports = s.Spec.Ports?.Select(p => $"{p.Port}/{p.Protocol}").ToList() ?? []
        };

    public async Task<List<DeploymentDetail>> ListDeploymentDetailsAllNamespacesAsync(
        CancellationToken cancellationToken = default)
    {
        var deployments = await _client.ListDeploymentForAllNamespacesAsync(
            cancellationToken: cancellationToken);
        return deployments.Items.Select(ToDeploymentDetail).ToList();
    }

    public async Task<List<DeploymentDetail>> ListDeploymentDetailsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default)
    {
        var result = new List<DeploymentDetail>();
        foreach (var namespaceName in namespaces.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var deployments = await _client.ListNamespacedDeploymentAsync(
                namespaceName, cancellationToken: cancellationToken);
            result.AddRange(deployments.Items.Select(ToDeploymentDetail));
        }
        return result;
    }

    private static DeploymentDetail ToDeploymentDetail(V1Deployment d) => new()
        {
            Name = d.Metadata.Name,
            Namespace = d.Metadata.NamespaceProperty ?? "",
            TemplateLabels = d.Spec.Template?.Metadata?.Labels is null
                ? []
                : new Dictionary<string, string>(d.Spec.Template.Metadata.Labels),
            Selector = d.Spec.Selector?.MatchLabels is null
                ? []
                : new Dictionary<string, string>(d.Spec.Selector.MatchLabels),
            Replicas = d.Spec.Replicas ?? 0,
            ReadyReplicas = d.Status.ReadyReplicas ?? 0,
            AvailableReplicas = d.Status.AvailableReplicas ?? 0,
            Revision = GetDictionaryValue(
                d.Metadata.Annotations, "deployment.kubernetes.io/revision"),
            Image = d.Spec?.Template?.Spec?.Containers?.FirstOrDefault()?.Image,
            Labels = d.Metadata.Labels is null ? [] : new Dictionary<string, string>(d.Metadata.Labels),
            Annotations = d.Metadata.Annotations is null ? [] : new Dictionary<string, string>(d.Metadata.Annotations)
        };

    public async Task<HashSet<string>> ListServicesWithReadyEndpointsAsync(
        CancellationToken cancellationToken = default)
    {
        var endpoints = await _client.ListEndpointsForAllNamespacesAsync(
            cancellationToken: cancellationToken);
        return ToReadyEndpointSet(endpoints.Items);
    }

    public async Task<HashSet<string>> ListServicesWithReadyEndpointsAsync(
        IEnumerable<string> namespaces,
        CancellationToken cancellationToken = default)
    {
        var items = new List<V1Endpoints>();
        foreach (var namespaceName in namespaces.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var endpoints = await _client.ListNamespacedEndpointsAsync(
                namespaceName, cancellationToken: cancellationToken);
            items.AddRange(endpoints.Items);
        }
        return ToReadyEndpointSet(items);
    }

    private static HashSet<string> ToReadyEndpointSet(IEnumerable<V1Endpoints> endpoints)
    {
        var ready = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ep in endpoints)
        {
            var hasAddress = ep.Subsets?.Any(s => s.Addresses is { Count: > 0 }) ?? false;
            if (hasAddress)
                ready.Add($"{ep.Metadata.NamespaceProperty}/{ep.Metadata.Name}");
        }
        return ready;
    }

    public async Task<Dictionary<string, string>?> GetConfigMapDataAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cm = await _client.ReadNamespacedConfigMapAsync(
                name, _namespace, cancellationToken: cancellationToken);
            return cm.Data is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(cm.Data);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task ReplaceConfigMapDataAsync(
        string name,
        Dictionary<string, string> data,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var cm = await _client.ReadNamespacedConfigMapAsync(
                name, _namespace, cancellationToken: cancellationToken);
            cm.Data = data;
            try
            {
                await _client.ReplaceNamespacedConfigMapAsync(
                    cm, name, _namespace, cancellationToken: cancellationToken);
                return;
            }
            catch (k8s.Autorest.HttpOperationException ex)
                when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict
                      && attempt < maxAttempts)
            {
                // Another writer bumped resourceVersion; re-read and retry.
            }
        }
    }

    private static string BuildLabelSelector(IReadOnlyDictionary<string, string> selector) =>
        string.Join(',', selector.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

    private static WorkloadPodDetail ToPodDetail(V1Pod pod)
    {
        var statuses = pod.Status.ContainerStatuses ?? [];
        var states = statuses.Select(c =>
        {
            if (c.State.Running is not null) return "Running";
            if (c.State.Waiting is not null) return $"Waiting: {c.State.Waiting.Reason}";
            if (c.State.Terminated is not null) return $"Terminated: {c.State.Terminated.Reason}";
            return "Unknown";
        }).ToList();

        var requests = new Dictionary<string, string>(StringComparer.Ordinal);
        var limits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var container in pod.Spec.Containers ?? [])
        {
            if (container.Resources?.Requests is { } resourceRequests)
            {
                foreach (var entry in resourceRequests)
                    requests[$"{container.Name}/{entry.Key}"] = entry.Value.ToString();
            }
            if (container.Resources?.Limits is { } resourceLimits)
            {
                foreach (var entry in resourceLimits)
                    limits[$"{container.Name}/{entry.Key}"] = entry.Value.ToString();
            }
        }

        return new WorkloadPodDetail
        {
            Name = pod.Metadata.Name,
            Phase = pod.Status.Phase ?? "Unknown",
            Ready = statuses.Count > 0 && statuses.All(c => c.Ready),
            Restarts = statuses.Sum(c => c.RestartCount),
            OomKilled = statuses.Any(c => c.State.Terminated?.Reason == "OOMKilled" ||
                                          c.LastState?.Terminated?.Reason == "OOMKilled"),
            CrashLoopBackOff = statuses.Any(c => c.State.Waiting?.Reason == "CrashLoopBackOff"),
            Pending = pod.Status.Phase == "Pending",
            ContainerStates = states,
            ResourceRequests = requests,
            ResourceLimits = limits
        };
    }

    private static string? GetDictionaryValue(
        IDictionary<string, string>? dictionary,
        string key) =>
        dictionary is not null && dictionary.TryGetValue(key, out var value) ? value : null;
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

public class ServiceDetail
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public Dictionary<string, string> Selector { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
    public Dictionary<string, string> Annotations { get; set; } = [];
    public List<string> Ports { get; set; } = [];
}

public class DeploymentDetail
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public Dictionary<string, string> TemplateLabels { get; set; } = [];
    public Dictionary<string, string> Selector { get; set; } = [];
    public Dictionary<string, string> Labels { get; set; } = [];
    public Dictionary<string, string> Annotations { get; set; } = [];
    public int Replicas { get; set; }
    public int ReadyReplicas { get; set; }
    public int AvailableReplicas { get; set; }
    public string? Revision { get; set; }
    public string? Image { get; set; }
}

public class WorkloadDetail
{
    public string DeploymentName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public int DesiredReplicas { get; set; }
    public int ReadyReplicas { get; set; }
    public int AvailableReplicas { get; set; }
    public string? Revision { get; set; }
    public string? Image { get; set; }
    public string? ImageDigest { get; set; }
    public Dictionary<string, string> Selector { get; set; } = [];
    public List<WorkloadPodDetail> Pods { get; set; } = [];
}

public class WorkloadPodDetail
{
    public string Name { get; set; } = "";
    public string Phase { get; set; } = "";
    public bool Ready { get; set; }
    public int Restarts { get; set; }
    public bool OomKilled { get; set; }
    public bool CrashLoopBackOff { get; set; }
    public bool Pending { get; set; }
    public List<string> ContainerStates { get; set; } = [];
    public Dictionary<string, string> ResourceRequests { get; set; } = [];
    public Dictionary<string, string> ResourceLimits { get; set; } = [];
}

public class KubernetesEventDetail
{
    public string Id { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string Type { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Message { get; set; } = "";
    public string InvolvedKind { get; set; } = "";
    public string InvolvedName { get; set; } = "";
    public int Count { get; set; }
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
