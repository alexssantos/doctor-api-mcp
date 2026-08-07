using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Anomalies;
using McpApis.McpServer.Providers.Contracts;

namespace McpApis.McpServer.Tests;

public sealed class AnomalyEngineTests
{
    private static readonly ServiceIdentity Service = new(
        "orders", "shop", "orders", "orders", "Orders", "orders", ["orders"]);

    [Fact]
    public async Task Known_latency_regression_is_detected_with_baseline_evidence()
    {
        var now = DateTimeOffset.UtcNow;
        var current = TimeWindow.EndingAt(now, TimeSpan.FromMinutes(15));
        var provider = new WindowAwareMetricsProvider(current, signal => signal switch
        {
            MetricSignal.P95Latency => (Baseline: 100d, Current: 280d),
            _ => (Baseline: 10d, Current: 10d)
        });

        var result = await new AnomalyEngine(provider).DetectAsync(Service, current);

        var anomaly = Assert.Single(result.Data.Anomalies,
            item => item.Metric == "latency_p95" && item.Conclusion == AnalysisConclusion.Detected);
        Assert.Equal(FindingSeverity.Critical, anomaly.Severity);
        Assert.Equal("robust_zscore+window_comparison", anomaly.Method);
        Assert.Equal(2, anomaly.EvidenceIds.Count);
        Assert.All(anomaly.EvidenceIds, id => Assert.Contains(result.Evidence, e => e.Id == id));
    }

    [Fact]
    public async Task Insufficient_samples_are_explicitly_inconclusive()
    {
        var current = TimeWindow.EndingAt(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        var provider = new WindowAwareMetricsProvider(
            current, _ => (Baseline: 10d, Current: 20d), pointsPerWindow: 1);

        var result = await new AnomalyEngine(provider).DetectAsync(Service, current);

        Assert.Equal(AnalysisConclusion.Inconclusive, result.Data.AnalysisConclusion);
        Assert.Equal(5, result.Data.Anomalies.Count);
        Assert.All(result.Data.Anomalies,
            anomaly => Assert.Equal(AnalysisConclusion.Inconclusive, anomaly.Conclusion));
    }

    [Fact]
    public async Task Isolated_traffic_spike_is_informational_not_an_incident_severity()
    {
        var current = TimeWindow.EndingAt(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));
        var provider = new WindowAwareMetricsProvider(current, signal => signal switch
        {
            MetricSignal.RequestRate => (Baseline: 10d, Current: 30d),
            _ => (Baseline: 10d, Current: 10d)
        });

        var result = await new AnomalyEngine(provider).DetectAsync(Service, current);

        var anomaly = Assert.Single(result.Data.Anomalies);
        Assert.Equal("request_rate", anomaly.Metric);
        Assert.Equal(FindingSeverity.Info, anomaly.Severity);
    }

    [Fact]
    public async Task Fully_unavailable_metrics_do_not_throw_and_preserve_source_status()
    {
        var current = TimeWindow.EndingAt(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));
        var result = await new AnomalyEngine(new UnavailableMetricsProvider())
            .DetectAsync(Service, current);

        Assert.Equal(AnalysisConclusion.Inconclusive, result.Data.AnalysisConclusion);
        var source = Assert.Single(result.Sources);
        Assert.Equal(SourceAvailability.Unavailable, source.Availability);
        Assert.Null(source.ObservedAt);
        Assert.Null(source.FreshnessSeconds);
    }

    private sealed class WindowAwareMetricsProvider(
        TimeWindow currentWindow,
        Func<MetricSignal, (double Baseline, double Current)> values,
        int pointsPerWindow = 4) : IMetricsProvider
    {
        public Task<ProviderResult<RedMetrics>> GetRedMetricsAsync(
            ServiceIdentity service,
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderResult<MetricSeries>> GetSeriesAsync(
            ServiceIdentity service,
            MetricSignal signal,
            TimeWindow window,
            CancellationToken cancellationToken = default)
        {
            var pair = values(signal);
            var isCurrent = window.From == currentWindow.From && window.To == currentWindow.To;
            var value = isCurrent ? pair.Current : pair.Baseline;
            var points = Enumerable.Range(0, pointsPerWindow)
                .Select(index => new MetricPoint(
                    window.From.AddTicks(window.Span.Ticks * index / Math.Max(pointsPerWindow, 1)),
                    value + (index % 2 == 0 ? -0.5 : 0.5)))
                .ToArray();
            var series = new MetricSeries(signal.ToString(), "value", "fixture", points);
            return Task.FromResult(ProviderResult<MetricSeries>.Available(
                "metrics", series, window.To, 1));
        }
    }

    private sealed class UnavailableMetricsProvider : IMetricsProvider
    {
        public Task<ProviderResult<RedMetrics>> GetRedMetricsAsync(
            ServiceIdentity service,
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderResult<MetricSeries>> GetSeriesAsync(
            ServiceIdentity service,
            MetricSignal signal,
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<MetricSeries>.Unavailable(
                "metrics", 1, "fixture_unavailable", "Metrics fixture is unavailable."));
    }
}
