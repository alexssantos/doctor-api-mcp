using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Health;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Providers.Contracts;

namespace McpApis.McpServer.Tests;

public sealed class HealthEngineTests
{
    private static readonly ServiceIdentity Service = new(
        "orders", "shop", "orders", "orders", "Orders", "orders", ["orders"]);
    private static readonly TimeWindow Window = TimeWindow.EndingAt(
        DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));

    [Fact]
    public async Task Zero_pods_is_never_healthy()
    {
        var engine = Engine(
            Metrics(errorRate: 0, latency: 20),
            Workload(hasPods: false, desired: 2, ready: 0));

        var result = await engine.EvaluateAsync(Service, Selector(), Window);

        Assert.Equal(HealthState.Critical, result.Data.HealthStatus);
        Assert.Contains(result.Data.Findings, finding => finding.Type == "availability");
        Assert.NotEqual(HealthState.Healthy, result.Data.HealthStatus);
    }

    [Fact]
    public async Task Missing_required_metrics_produces_unknown_and_reduced_coverage()
    {
        var engine = Engine(
            new RedMetrics(null, null, null, null, null,
                Measurement("availability", 1, "ratio"), null, null),
            Workload(hasPods: true, desired: 2, ready: 2));

        var result = await engine.EvaluateAsync(Service, Selector(), Window);

        Assert.Equal(HealthState.Unknown, result.Data.HealthStatus);
        Assert.True(result.Data.Coverage < 0.8);
        Assert.Contains(result.Data.Dimensions, d => d.Name == "errors" && d.Score is null && d.Required);
    }

    [Fact]
    public async Task Score_and_findings_are_deterministic_for_thresholds()
    {
        var metrics = Metrics(errorRate: 0.12, latency: 2400);
        var workload = Workload(hasPods: true, desired: 2, ready: 2, restarts: 6);
        var first = await Engine(metrics, workload).EvaluateAsync(Service, Selector(), Window);
        var second = await Engine(metrics, workload).EvaluateAsync(Service, Selector(), Window);

        Assert.Equal(HealthState.Critical, first.Data.HealthStatus);
        Assert.Equal(first.Data.Score, second.Data.Score);
        Assert.Equal(first.Data.Coverage, second.Data.Coverage);
        Assert.Contains(first.Data.Findings, f => f.Type == "error_rate" && f.Severity == FindingSeverity.Critical);
        Assert.All(first.Data.Findings, finding => Assert.NotEmpty(finding.EvidenceIds));
    }

    private static HealthEngine Engine(RedMetrics metrics, KubernetesWorkloadState workload) =>
        new(
            new FakeMetricsProvider(metrics),
            new FakeKubernetesProvider(workload),
            Options.Create(new HealthEngineOptions()));

    private static RedMetrics Metrics(double errorRate, double latency) => new(
        Measurement("request_rate", 10, "requests/s"),
        Measurement("error_rate", errorRate, "ratio"),
        Measurement("latency_p50", latency / 2, "ms"),
        Measurement("latency_p95", latency, "ms"),
        Measurement("latency_p99", latency * 1.2, "ms"),
        Measurement("availability", 1, "ratio"),
        Measurement("cpu_usage", 0.2, "cores"),
        Measurement("memory_usage", 128 * 1024 * 1024, "bytes"));

    private static KubernetesWorkloadState Workload(
        bool hasPods,
        int desired,
        int ready,
        int restarts = 0)
    {
        var pods = hasPods
            ? Enumerable.Range(0, Math.Max(desired, 1))
                .Select(i => new KubernetesPodState(
                    $"orders-{i}", "Running", i < ready, i == 0 ? restarts : 0,
                    false, false, false, ["Running"], new Dictionary<string, string>(),
                    new Dictionary<string, string>()))
                .ToArray()
            : [];
        return new KubernetesWorkloadState(
            "orders", desired, ready, ready, "1", "orders:1.0", null,
            Selector(), pods, restarts, hasPods && ready == desired, hasPods);
    }

    private static Measurement Measurement(string metric, double value, string unit) =>
        new(metric, value, unit, DateTimeOffset.UtcNow, "test");

    private static Dictionary<string, string> Selector() => new() { ["app"] = "orders" };

    private sealed class FakeMetricsProvider(RedMetrics metrics) : IMetricsProvider
    {
        public Task<ProviderResult<RedMetrics>> GetRedMetricsAsync(
            ServiceIdentity service,
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<RedMetrics>.Available(
                "metrics", metrics, DateTimeOffset.UtcNow, 1));

        public Task<ProviderResult<MetricSeries>> GetSeriesAsync(
            ServiceIdentity service,
            MetricSignal signal,
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeKubernetesProvider(KubernetesWorkloadState workload) : IKubernetesProvider
    {
        public Task<ProviderResult<KubernetesWorkloadState>> GetWorkloadStateAsync(
            ServiceIdentity service,
            IReadOnlyDictionary<string, string> selector,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<KubernetesWorkloadState>.Available(
                "kubernetes", workload, DateTimeOffset.UtcNow, 1));

        public Task<ProviderResult<IReadOnlyList<KubernetesEventRecord>>> GetEventsAsync(
            ServiceIdentity service,
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
