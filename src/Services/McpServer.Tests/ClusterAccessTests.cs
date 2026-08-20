using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services;
using System.Text.Json;

namespace McpApis.McpServer.Tests;

public sealed class ClusterAccessTests
{
    [Fact]
    public async Task Restricted_mode_needs_no_kubernetes_permissions()
    {
        var validator = CreateValidator(
            new ClusterAccessOptions
            {
                Scope = ClusterAccessScope.None,
                ServiceDiscovery = false,
                StateStorage = ClusterStateStorage.Memory,
                AllowVolumes = false
            },
            services: new Dictionary<string, string?>
            {
                ["Services:fixture"] = "http://fixture.restricted.svc.cluster.local"
            },
            features: new ObservabilityFeatureOptions { EnableDeploymentEvents = false });

        var report = await validator.ValidateAsync();

        Assert.True(report.MeetsMinimumRequirements);
        Assert.Equal("restricted", report.Mode);
        Assert.Empty(report.MissingRequirements);
        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"scope\":\"None\"", json);
        Assert.Contains("\"stateStorage\":\"Memory\"", json);
    }

    [Fact]
    public async Task Disabled_discovery_requires_an_explicit_service()
    {
        var validator = CreateValidator(
            new ClusterAccessOptions
            {
                Scope = ClusterAccessScope.None,
                ServiceDiscovery = false,
                StateStorage = ClusterStateStorage.Memory,
                AllowVolumes = false
            },
            features: new ObservabilityFeatureOptions { EnableDeploymentEvents = false });

        var report = await validator.ValidateAsync();

        Assert.False(report.MeetsMinimumRequirements);
        Assert.Contains("explicit-service-configuration", report.MissingRequirements);
    }

    [Fact]
    public async Task Namespace_mode_checks_only_namespaced_permissions()
    {
        var collector = new NamespacedKubernetesCollector();
        var validator = CreateValidator(
            new ClusterAccessOptions
            {
                Scope = ClusterAccessScope.Namespace,
                ServiceDiscovery = true,
                StateStorage = ClusterStateStorage.ConfigMap
            },
            collector: collector);

        var report = await validator.ValidateAsync();

        Assert.True(report.MeetsMinimumRequirements);
        Assert.Contains(collector.Reviews, review => review.Resource == "services");
        Assert.Contains(collector.Reviews, review =>
            review.Resource == "configmaps" && review.ResourceName == "mcpserver-state");
        Assert.Contains(collector.Reviews, review =>
            review.Resource == "pods" && review.Namespace is null);
    }

    [Fact]
    public async Task Namespace_mode_rejects_cluster_wide_permissions()
    {
        var validator = CreateValidator(
            new ClusterAccessOptions
            {
                Scope = ClusterAccessScope.Namespace,
                ServiceDiscovery = true,
                StateStorage = ClusterStateStorage.ConfigMap
            },
            collector: new AllowingKubernetesCollector());

        var report = await validator.ValidateAsync();

        Assert.False(report.MeetsMinimumRequirements);
        Assert.Contains("pods-cluster-list-denied", report.MissingRequirements);
    }

    [Fact]
    public async Task Disabled_service_discovery_requires_service_permissions_to_be_absent()
    {
        var collector = new NoDiscoveryKubernetesCollector();
        var validator = CreateValidator(
            new ClusterAccessOptions
            {
                Scope = ClusterAccessScope.Namespace,
                ServiceDiscovery = false,
                StateStorage = ClusterStateStorage.ConfigMap
            },
            services: new Dictionary<string, string?>
            {
                ["Services:fixture"] = "http://fixture.apps.svc.cluster.local"
            },
            collector: collector);

        var report = await validator.ValidateAsync();

        Assert.True(report.MeetsMinimumRequirements);
        Assert.Contains(collector.Reviews, review => review.Resource == "endpoints");
    }

    [Fact]
    public async Task Memory_indexing_state_is_process_local()
    {
        var store = new InMemoryIndexingStateStore();

        var persisted = await store.SaveAsync("apps/catalog", true);
        var values = await store.LoadAsync();

        Assert.False(persisted);
        Assert.True(values["apps/catalog"]);
    }

    private static ClusterRequirementsValidator CreateValidator(
        ClusterAccessOptions access,
        IReadOnlyDictionary<string, string?>? services = null,
        ObservabilityFeatureOptions? features = null,
        DisabledKubernetesCollector? collector = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["DataSources:Kubernetes:Namespace"] = "apps",
            ["Discovery:StateConfigMap"] = "mcpserver-state"
        };
        if (services is not null)
            foreach (var (key, value) in services)
                values[key] = value;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new ClusterRequirementsValidator(
            collector ?? new DisabledKubernetesCollector(),
            configuration,
            Options.Create(access),
            Options.Create(new SecurityOptions { AllowedNamespaces = ["apps"] }),
            Options.Create(features ?? new ObservabilityFeatureOptions()),
            NullLogger<ClusterRequirementsValidator>.Instance);
    }

    private sealed class AllowingKubernetesCollector : DisabledKubernetesCollector
    {
        public List<Review> Reviews { get; } = [];

        public override Task<bool> CanIAsync(
            string verb,
            string apiGroup,
            string resource,
            string? namespaceName = null,
            string? resourceName = null,
            CancellationToken cancellationToken = default)
        {
            Reviews.Add(new Review(verb, resource, namespaceName, resourceName));
            return Task.FromResult(true);
        }
    }

    private sealed class NamespacedKubernetesCollector : DisabledKubernetesCollector
    {
        public List<Review> Reviews { get; } = [];

        public override Task<bool> CanIAsync(
            string verb,
            string apiGroup,
            string resource,
            string? namespaceName = null,
            string? resourceName = null,
            CancellationToken cancellationToken = default)
        {
            Reviews.Add(new Review(verb, resource, namespaceName, resourceName));
            return Task.FromResult(namespaceName is not null);
        }
    }

    private sealed class NoDiscoveryKubernetesCollector : DisabledKubernetesCollector
    {
        public List<Review> Reviews { get; } = [];

        public override Task<bool> CanIAsync(
            string verb,
            string apiGroup,
            string resource,
            string? namespaceName = null,
            string? resourceName = null,
            CancellationToken cancellationToken = default)
        {
            Reviews.Add(new Review(verb, resource, namespaceName, resourceName));
            var denied = namespaceName is null || resource is "services" or "endpoints";
            return Task.FromResult(!denied);
        }
    }

    private sealed record Review(
        string Verb,
        string Resource,
        string? Namespace,
        string? ResourceName);
}
