using Microsoft.Extensions.Options;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Validates the effective service-account permissions and configuration for
/// the selected installation mode. Results are cached because readiness probes
/// call this service repeatedly.
/// </summary>
public sealed class ClusterRequirementsValidator(
    IKubernetesCollector kubernetes,
    IConfiguration configuration,
    IOptions<ClusterAccessOptions> clusterAccess,
    IOptions<SecurityOptions> security,
    IOptions<ObservabilityFeatureOptions> features,
    ILogger<ClusterRequirementsValidator> logger) : IClusterRequirementsValidator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClusterRequirementsReport? _cached;

    public async Task<ClusterRequirementsReport> ValidateAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var options = clusterAccess.Value;
        if (!forceRefresh && _cached is not null &&
            DateTimeOffset.UtcNow - _cached.CheckedAt <
            TimeSpan.FromSeconds(options.ValidationCacheSeconds))
            return _cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cached is not null &&
                DateTimeOffset.UtcNow - _cached.CheckedAt <
                TimeSpan.FromSeconds(options.ValidationCacheSeconds))
                return _cached;

            _cached = await ValidateCoreAsync(options, cancellationToken);
            if (!_cached.MeetsMinimumRequirements)
            {
                logger.LogError(
                    "Cluster access requirements are not satisfied for mode {Mode}: {Missing}.",
                    _cached.Mode,
                    string.Join(", ", _cached.MissingRequirements));
            }
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ClusterRequirementsReport> ValidateCoreAsync(
        ClusterAccessOptions options,
        CancellationToken cancellationToken)
    {
        var checks = new List<ClusterRequirementCheck>();
        var configuredNamespace =
            configuration["DataSources:Kubernetes:Namespace"] ?? "mcp-apis";
        var allowedNamespaces = security.Value.AllowedNamespaces;
        var configuredServices = configuration.GetSection("Services").GetChildren()
            .Count(child => !string.IsNullOrWhiteSpace(child.Value));

        Add("allowed-namespaces", allowedNamespaces.Length > 0,
            allowedNamespaces.Length > 0
                ? $"Allowed namespaces: {string.Join(", ", allowedNamespaces)}."
                : "At least one Security:AllowedNamespaces entry is required.");

        if (options.Scope == ClusterAccessScope.Namespace)
        {
            var valid = allowedNamespaces.Length == 1 &&
                        allowedNamespaces[0].Equals(
                            configuredNamespace, StringComparison.OrdinalIgnoreCase);
            Add("single-namespace-contract", valid,
                valid
                    ? $"Access is constrained to namespace '{configuredNamespace}'."
                    : "Namespace scope requires exactly one allowed namespace matching DataSources:Kubernetes:Namespace.");
        }

        if (!options.ServiceDiscovery)
        {
            Add("explicit-service-configuration", configuredServices > 0,
                configuredServices > 0
                    ? $"{configuredServices} explicit service endpoint(s) configured."
                    : "Service discovery is disabled; configure at least one Services entry.");
        }

        if (options.Scope == ClusterAccessScope.None)
        {
            Add("kubernetes-api-disabled", !options.ServiceDiscovery,
                "No Kubernetes API token or RBAC is required.");
            const string serviceAccountToken =
                "/var/run/secrets/kubernetes.io/serviceaccount/token";
            var tokenAbsent = !File.Exists(serviceAccountToken);
            Add("service-account-token-absent", tokenAbsent,
                tokenAbsent
                    ? "The projected Kubernetes ServiceAccount token is not mounted."
                    : "Scope None requires automountServiceAccountToken=false.");
            Add("memory-state", options.StateStorage == ClusterStateStorage.Memory,
                "Scope None requires pod-local memory state.");
            Add("deployment-events-disabled", !features.Value.EnableDeploymentEvents,
                "Kubernetes deployment/events integration must be disabled without API access.");
            return Build(options, checks);
        }

        if (options.StateStorage == ClusterStateStorage.ConfigMap)
            Add("configmap-state-compatible", true,
                "ConfigMap persistence is compatible with the selected Kubernetes API scope.");

        var reviewNamespace = options.Scope == ClusterAccessScope.Cluster
            ? null
            : configuredNamespace;
        await AddAccess("pods-list", "list", "", "pods", reviewNamespace);
        await AddAccess("pods-get", "get", "", "pods", reviewNamespace);
        await AddAccess("deployments-list", "list", "apps", "deployments", reviewNamespace);
        await AddAccess("deployments-get", "get", "apps", "deployments", reviewNamespace);

        if (features.Value.EnableDeploymentEvents)
        {
            await AddAccess("events-list", "list", "", "events", reviewNamespace);
            await AddAccess("events-get", "get", "", "events", reviewNamespace);
        }

        if (options.ServiceDiscovery)
        {
            await AddAccess("services-list", "list", "", "services", reviewNamespace);
            await AddAccess("services-get", "get", "", "services", reviewNamespace);
            await AddAccess("endpoints-list", "list", "", "endpoints", reviewNamespace);
            await AddAccess("endpoints-get", "get", "", "endpoints", reviewNamespace);
        }
        else
        {
            await AddAccess("services-list-denied", "list", "", "services",
                reviewNamespace, expectedAllowed: false);
            await AddAccess("services-get-denied", "get", "", "services",
                reviewNamespace, expectedAllowed: false);
            await AddAccess("endpoints-list-denied", "list", "", "endpoints",
                reviewNamespace, expectedAllowed: false);
            await AddAccess("endpoints-get-denied", "get", "", "endpoints",
                reviewNamespace, expectedAllowed: false);
        }

        if (options.Scope == ClusterAccessScope.Namespace)
        {
            await AddAccess("pods-cluster-list-denied", "list", "", "pods", null,
                expectedAllowed: false);
            await AddAccess("deployments-cluster-list-denied", "list", "apps", "deployments", null,
                expectedAllowed: false);
            if (features.Value.EnableDeploymentEvents)
                await AddAccess("events-cluster-list-denied", "list", "", "events", null,
                    expectedAllowed: false);
            if (options.ServiceDiscovery)
            {
                await AddAccess("services-cluster-list-denied", "list", "", "services", null,
                    expectedAllowed: false);
                await AddAccess("endpoints-cluster-list-denied", "list", "", "endpoints", null,
                    expectedAllowed: false);
            }
        }

        if (options.StateStorage == ClusterStateStorage.ConfigMap)
        {
            var stateName = configuration["Discovery:StateConfigMap"] ?? "mcpserver-state";
            await AddAccess("state-configmap-get", "get", "", "configmaps",
                configuredNamespace, stateName);
            await AddAccess("state-configmap-update", "update", "", "configmaps",
                configuredNamespace, stateName);
            await AddAccess("state-configmap-patch", "patch", "", "configmaps",
                configuredNamespace, stateName);
        }

        return Build(options, checks);

        void Add(string name, bool satisfied, string detail) =>
            checks.Add(new ClusterRequirementCheck(name, true, satisfied, detail));

        async Task AddAccess(
            string name,
            string verb,
            string apiGroup,
            string resource,
            string? namespaceName,
            string? resourceName = null,
            bool expectedAllowed = true)
        {
            try
            {
                var allowed = await kubernetes.CanIAsync(
                    verb, apiGroup, resource, namespaceName, resourceName, cancellationToken);
                var target = namespaceName is null ? "cluster" : $"namespace/{namespaceName}";
                var satisfied = allowed == expectedAllowed;
                Add(name, satisfied,
                    satisfied
                        ? expectedAllowed
                            ? $"ServiceAccount can {verb} {resource} in {target}."
                            : $"ServiceAccount is denied {verb} {resource} in {target}, as required."
                        : expectedAllowed
                            ? $"ServiceAccount cannot {verb} {resource} in {target}."
                            : $"ServiceAccount can {verb} {resource} in {target}, exceeding the declared restriction.");
            }
            catch (Exception ex)
            {
                Add(name, false, $"Kubernetes authorization review failed: {ex.Message}");
            }
        }
    }

    private static ClusterRequirementsReport Build(
        ClusterAccessOptions options,
        IReadOnlyList<ClusterRequirementCheck> checks) =>
        new(
            options.EffectiveMode,
            options.Scope,
            options.ServiceDiscovery,
            options.StateStorage,
            options.AllowVolumes,
            checks.All(check => !check.Required || check.Satisfied),
            DateTimeOffset.UtcNow,
            checks);
}
