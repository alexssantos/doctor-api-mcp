using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Discovers candidate services from one or more sources based on Discovery:Mode config.
///
/// Modes:
///   Config     – reads "Services" config section (e.g. Services__precoapi=http://precoapi)
///   Kubernetes – lists K8s services labelled with Discovery:KubernetesLabel=true
///   Both       – merges both sources (K8s entries override config entries of the same name)
/// </summary>
public class ServiceDiscoveryService : IServiceDiscovery
{
    private readonly IConfiguration _config;
    private readonly IKubernetesCollector _k8s;
    private readonly ILogger<ServiceDiscoveryService> _logger;

    public ServiceDiscoveryService(
        IConfiguration config,
        IKubernetesCollector k8s,
        ILogger<ServiceDiscoveryService> logger)
    {
        _config = config;
        _k8s = k8s;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> DiscoverServicesAsync()
    {
        var mode = _config["Discovery:Mode"] ?? "Config";
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (mode is "Config" or "Both")
            DiscoverFromConfig(result);

        if (mode is "Kubernetes" or "Both")
            await DiscoverFromKubernetesAsync(result);

        _logger.LogInformation(
            "Service discovery ({Mode}) found {Count} candidate(s): {Names}",
            mode, result.Count, string.Join(", ", result.Keys));

        return result;
    }

    private void DiscoverFromConfig(Dictionary<string, string> result)
    {
        var section = _config.GetSection("Services");
        foreach (var child in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Value))
                continue;

            result[child.Key] = child.Value;

            // Warn about short hostnames — they only resolve within the same namespace.
            // Use FQDNs (http://<service>.<namespace>.svc.cluster.local) to reach
            // services in other namespaces.
            if (Uri.TryCreate(child.Value, UriKind.Absolute, out var uri)
                && !uri.Host.Contains('.'))
            {
                _logger.LogWarning(
                    "Service '{Name}' uses short hostname '{Host}'. " +
                    "This only resolves within the same namespace. " +
                    "Use http://{Host}.<namespace>.svc.cluster.local for cross-namespace access.",
                    child.Key, uri.Host, uri.Host);
            }
        }
    }

    private async Task DiscoverFromKubernetesAsync(Dictionary<string, string> result)
    {
        var labelKey = _config["Discovery:KubernetesLabel"] ?? "mcp-apis/indexed";
        try
        {
            var discovered = await _k8s.DiscoverIndexedServicesAsync(labelKey);
            foreach (var (name, url) in discovered)
                result[name] = url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Kubernetes service discovery failed (label: {Label}). Continuing without it.",
                labelKey);
        }
    }
}
