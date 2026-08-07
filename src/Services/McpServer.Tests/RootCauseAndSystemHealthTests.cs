using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Correlation;
using McpApis.McpServer.Engines.Dependencies;
using McpApis.McpServer.Engines.Health;
using McpApis.McpServer.Engines.Recommendations;
using McpApis.McpServer.Engines.RootCause;
using McpApis.McpServer.Engines.SystemHealth;
using McpApis.McpServer.Infrastructure.Caching;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Tests;

public sealed class RootCauseAndSystemHealthTests
{
    private static readonly ServiceIdentity Service = new(
        "orders", "shop", "orders", "orders", "Orders", "orders", ["orders"]);

    [Fact]
    public async Task Deployment_before_regression_becomes_evidence_backed_primary_hypothesis()
    {
        var now = DateTimeOffset.UtcNow;
        var window = TimeWindow.EndingAt(now, TimeSpan.FromMinutes(30));
        var deploymentAt = now.AddMinutes(-12);
        var incidentAt = now.AddMinutes(-10);
        var deploymentEvidence = Evidence("deploy", "deployments", deploymentAt);
        var latencyEvidence = Evidence("latency", "metrics", incidentAt);
        var timeline = new IncidentTimeline(
            AnalysisConclusion.Detected,
            incidentAt,
            [
                new IncidentEvent(
                    "deploy-event", deploymentAt, "version_change", Service,
                    FindingSeverity.Info, "deployments", "Revision changed to 4.",
                    [deploymentEvidence.Id]),
                new IncidentEvent(
                    "latency-event", incidentAt, "latency_p95_anomaly", Service,
                    FindingSeverity.Warning, "metrics", "P95 regressed from its baseline.",
                    [latencyEvidence.Id])
            ],
            ["deployment_preceded_incident_by_120s:deploy-event"]);
        var timelineResult = new AnalysisResult<IncidentTimeline>(
            timeline,
            [
                Status("metrics"), Status("kubernetes"), Status("events"),
                Status("traces"), Status("logs"), Status("deployments")
            ],
            [deploymentEvidence, latencyEvidence],
            []);
        var healthResult = HealthResult(HealthState.Degraded, 72, 1);
        var graph = EmptyGraph();
        var engine = new RootCauseEngine(
            new FakeCorrelationEngine(timelineResult),
            new FakeHealthAnalysis(healthResult),
            new FakeDependencyEngine(new AnalysisResult<DependencyGraph>(
                graph, [Status("traces")], [], [])),
            new RecommendationEngine());

        var result = await engine.AnalyzeAsync(
            Service, new Dictionary<string, string> { ["app"] = "orders" }, window, 2);

        Assert.Equal(AnalysisConclusion.Detected, result.Data.AnalysisConclusion);
        Assert.Equal("recent_deployment", result.Data.PrimaryHypothesis?.Id);
        Assert.True(result.Data.PrimaryHypothesis?.Confidence >= 0.5);
        Assert.Contains(deploymentEvidence.Id,
            result.Data.PrimaryHypothesis!.SupportingEvidenceIds);
        Assert.Contains(latencyEvidence.Id,
            result.Data.PrimaryHypothesis.SupportingEvidenceIds);
        Assert.NotEmpty(result.Data.Recommendations);
        Assert.All(result.Data.Recommendations,
            recommendation => Assert.False(recommendation.Executable));
        Assert.All(result.Data.PrimaryHypothesis.SupportingEvidenceIds,
            id => Assert.Contains(result.Evidence, evidence => evidence.Id == id));
    }

    [Fact]
    public async Task Weak_single_source_incident_is_inconclusive_instead_of_inventing_a_cause()
    {
        var now = DateTimeOffset.UtcNow;
        var window = TimeWindow.EndingAt(now, TimeSpan.FromMinutes(30));
        var logEvidence = Evidence("log", "logs", now.AddMinutes(-2));
        var timeline = new IncidentTimeline(
            AnalysisConclusion.Detected,
            now.AddMinutes(-2),
            [new IncidentEvent(
                "log-event", now.AddMinutes(-2), "log_error_pattern", Service,
                FindingSeverity.Warning, "logs", "A redacted error pattern was observed.",
                [logEvidence.Id])],
            []);
        var unavailable = new[]
        {
            Unavailable("metrics"), Unavailable("kubernetes"), Unavailable("events"),
            Unavailable("traces"), Status("logs"), Unavailable("deployments")
        };
        var unavailableHealth = new AnalysisResult<HealthReport>(
            new HealthReport(HealthState.Unknown, null, 0, [], [], now),
            [Unavailable("metrics"), Unavailable("kubernetes")], [], []);
        var engine = new RootCauseEngine(
            new FakeCorrelationEngine(new AnalysisResult<IncidentTimeline>(
                timeline, unavailable, [logEvidence], [])),
            new FakeHealthAnalysis(unavailableHealth),
            new FakeDependencyEngine(new AnalysisResult<DependencyGraph>(
                EmptyGraph(), [Unavailable("traces")], [], [])),
            new RecommendationEngine());

        var result = await engine.AnalyzeAsync(
            Service, new Dictionary<string, string>(), window, 2);

        Assert.Equal(AnalysisConclusion.Inconclusive, result.Data.AnalysisConclusion);
        Assert.Null(result.Data.PrimaryHypothesis);
        Assert.True(result.Data.Coverage < 0.5);
        Assert.Contains(result.Data.Limitations,
            limitation => limitation.Contains("confidence threshold", StringComparison.Ordinal));
    }

    [Fact]
    public async Task System_summary_reuses_the_exact_service_health_cache_entry()
    {
        var catalog = new ApplicationCatalog();
        catalog.ReplaceSnapshot([Application()], TimeSpan.FromMinutes(5));
        var resolver = new ServiceIdentityResolver(
            catalog,
            Options.Create(new SecurityOptions { AllowedNamespaces = ["shop"] }));
        var engine = new CountingHealthEngine(HealthResult(HealthState.Healthy, 96, 1));
        var cache = new ObservabilityCache();
        var health = new HealthAnalysisService(
            engine, cache,
            Options.Create(new ObservabilityCacheOptions { HealthTtlSeconds = 60 }));
        var system = new SystemHealthEngine(
            catalog, resolver, health,
            Options.Create(new ObservabilityLimitsOptions { ConcurrencyLimit = 4 }));
        var window = TimeWindow.EndingAt(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

        await health.EvaluateAsync(Service, Application().Selector, window);
        var result = await system.SummarizeAsync(window);

        Assert.Equal(1, engine.Calls);
        Assert.Equal(HealthState.Healthy, result.Data.HealthStatus);
        Assert.Equal(1, result.Data.TotalServices);
        Assert.Equal(1, result.Data.Healthy);
        Assert.Single(result.Data.Services);
    }

    private static Evidence Evidence(string id, string source, DateTimeOffset timestamp) =>
        new(id, source, id, 1, null, "count", timestamp, "fixture");

    private static SourceStatus Status(string name) =>
        new(name, SourceAvailability.Available, DateTimeOffset.UtcNow, 0, 1, []);

    private static SourceStatus Unavailable(string name) =>
        new(name, SourceAvailability.Unavailable, null, null, 1, ["fixture unavailable"]);

    private static AnalysisResult<HealthReport> HealthResult(
        HealthState state,
        double? score,
        double coverage)
    {
        var report = new HealthReport(
            state, score, coverage, [], [], DateTimeOffset.UtcNow);
        return new AnalysisResult<HealthReport>(
            report, [Status("metrics"), Status("kubernetes")], [], []);
    }

    private static DependencyGraph EmptyGraph() =>
        new(Service, 2, [Service], [], [], [], [Service.Key], []);

    private static DiscoveredApplication Application() => new()
    {
        Name = "orders",
        Namespace = "shop",
        DeploymentName = "orders",
        KubernetesServiceName = "orders",
        OtelServiceName = "Orders",
        MetricsId = "orders",
        Selector = new Dictionary<string, string> { ["app"] = "orders" },
        Sources = DiscoverySources.Deployment | DiscoverySources.Network,
        OpenApi = OpenApiInfo.NotValidated,
        Enabled = true,
        FirstSeen = DateTimeOffset.UtcNow,
        LastSeen = DateTimeOffset.UtcNow
    };

    private sealed class FakeCorrelationEngine(AnalysisResult<IncidentTimeline> result)
        : ICorrelationEngine
    {
        public Task<AnalysisResult<IncidentTimeline>> BuildTimelineAsync(
            ServiceIdentity service,
            IReadOnlyDictionary<string, string> selector,
            TimeWindow window,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeHealthAnalysis(AnalysisResult<HealthReport> result)
        : IHealthAnalysisService
    {
        public Task<AnalysisResult<HealthReport>> EvaluateAsync(
            ServiceIdentity service,
            IReadOnlyDictionary<string, string> selector,
            TimeWindow window,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeDependencyEngine(AnalysisResult<DependencyGraph> result)
        : IDependencyEngine
    {
        public Task<AnalysisResult<DependencyGraph>> AnalyzeAsync(
            ServiceIdentity root,
            TimeWindow window,
            int depth,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class CountingHealthEngine(AnalysisResult<HealthReport> result) : IHealthEngine
    {
        private int _calls;
        public int Calls => _calls;

        public Task<AnalysisResult<HealthReport>> EvaluateAsync(
            ServiceIdentity service,
            IReadOnlyDictionary<string, string> selector,
            TimeWindow window,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(result);
        }
    }
}
