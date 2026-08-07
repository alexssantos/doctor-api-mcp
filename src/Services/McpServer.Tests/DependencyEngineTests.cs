using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Dependencies;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Tests;

public sealed class DependencyEngineTests
{
    [Fact]
    public async Task Graph_preserves_cycles_honors_depth_and_does_not_leak_disabled_services()
    {
        var catalog = new ApplicationCatalog();
        catalog.ReplaceSnapshot(
            [App("a"), App("b"), App("c"), App("hidden") with { Enabled = false }],
            TimeSpan.FromMinutes(5));
        var resolver = new ServiceIdentityResolver(
            catalog,
            Options.Create(new SecurityOptions { AllowedNamespaces = ["apps"] }));
        var observations = new[]
        {
            Observation("a", "b", 100, 10),
            Observation("b", "c", 80, 20),
            Observation("c", "a", 60, 5),
            Observation("c", "hidden", 999, 1)
        };
        var engine = new DependencyEngine(
            new FakeTraceProvider(observations), catalog, resolver,
            Options.Create(new ObservabilityLimitsOptions { MaxGraphDepth = 4 }));
        var root = resolver.Resolve("a", "apps").Identity!;

        var result = await engine.AnalyzeAsync(
            root, TimeWindow.EndingAt(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)), 2);

        Assert.Contains(result.Data.Nodes, node => node.ServiceName == "a");
        Assert.Contains(result.Data.Nodes, node => node.ServiceName == "b");
        Assert.Contains(result.Data.Nodes, node => node.ServiceName == "c");
        Assert.DoesNotContain(result.Data.Nodes, node => node.ServiceName == "hidden");
        Assert.NotEmpty(result.Data.Cycles);
        Assert.All(result.Data.Inbound.Concat(result.Data.Outbound), edge => Assert.NotEmpty(edge.EvidenceIds));
        Assert.True(result.Data.CriticalPath.Count <= 3);
    }

    private static DependencyObservation Observation(
        string source,
        string target,
        long calls,
        double latency) =>
        new(source, target, DateTimeOffset.UtcNow, calls, 0, latency, "fixture", []);

    private static DiscoveredApplication App(string name) => new()
    {
        Name = name,
        Namespace = "apps",
        DeploymentName = name,
        KubernetesServiceName = name,
        OtelServiceName = name,
        MetricsId = name,
        Sources = DiscoverySources.Deployment | DiscoverySources.OpenTelemetry,
        OpenApi = OpenApiInfo.NotValidated,
        Enabled = true,
        FirstSeen = DateTimeOffset.UtcNow,
        LastSeen = DateTimeOffset.UtcNow
    };

    private sealed class FakeTraceProvider(IReadOnlyList<DependencyObservation> observations) : ITraceProvider
    {
        public Task<ProviderResult<IReadOnlyList<NormalizedSpan>>> GetSpansAsync(
            ServiceIdentity service,
            TimeWindow window,
            int maxTraces,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderResult<IReadOnlyList<DependencyObservation>>> GetDependenciesAsync(
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<DependencyObservation>>.Available(
                "traces", observations, DateTimeOffset.UtcNow, 1));
    }
}
