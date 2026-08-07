using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Anomalies;
using McpApis.McpServer.Engines.Correlation;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Providers.Loki;

namespace McpApis.McpServer.Tests;

public sealed class LogsAndCorrelationTests
{
    private static readonly ServiceIdentity Service = new(
        "orders", "shop", "orders", "orders", "Orders", "orders", ["orders"]);

    [Fact]
    public async Task Loki_uses_internal_selector_groups_fingerprints_and_redacts_pii()
    {
        var now = DateTimeOffset.UtcNow;
        var nanoseconds = now.ToUnixTimeMilliseconds() * 1_000_000;
        var payload = """
            {"status":"success","data":{"resultType":"streams","result":[
              {"stream":{"namespace":"shop","pod":"orders-abc"},"values":[
                ["__NOW__","{\"level\":\"Error\",\"message\":\"Failed order 123 for ana@example.com\",\"traceId\":\"0123456789abcdef0123456789abcdef\"}"],
                ["__BEFORE__","{\"level\":\"Error\",\"message\":\"Failed order 456 for bia@example.com\",\"traceId\":\"0123456789abcdef0123456789abcdef\"}"]
              ]}
            ]}}
            """
            .Replace("__NOW__", nanoseconds.ToString(), StringComparison.Ordinal)
            .Replace("__BEFORE__", (nanoseconds - 1_000_000).ToString(), StringComparison.Ordinal);
        var handler = new RecordingHandler(payload);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://loki/") };
        var provider = new LokiLogsProvider(
            client,
            Options.Create(new ObservabilityLimitsOptions()),
            Options.Create(new ObservabilityFeatureOptions()),
            NullLogger<LokiLogsProvider>.Instance);

        var result = await provider.GetErrorPatternsAsync(
            Service, TimeWindow.EndingAt(now, TimeSpan.FromMinutes(15)), 20);

        var pattern = Assert.Single(result.Value!);
        Assert.Equal(2, pattern.Count);
        Assert.True(pattern.Redacted);
        Assert.DoesNotContain("ana@example.com", pattern.Message);
        Assert.DoesNotContain("bia@example.com", pattern.Message);
        var decodedQuery = Uri.UnescapeDataString(handler.LastRequest!.Query);
        Assert.Contains("namespace=\"shop\"", decodedQuery);
        Assert.Contains("pod=~\"^(?:orders)", decodedQuery);
        Assert.Contains("error|exception|fail", decodedQuery);
    }

    [Fact]
    public async Task Correlation_orders_sources_and_marks_missing_logs_as_partial()
    {
        var now = DateTimeOffset.UtcNow;
        var window = TimeWindow.EndingAt(now, TimeSpan.FromMinutes(30));
        var anomalyAt = now.AddMinutes(-10);
        var anomalyEvidence = new Evidence(
            "metrics:latency:current", "metrics", "latency_p95", 900, 200, "ms",
            anomalyAt, "fixture");
        var anomaly = new Anomaly(
            "latency_p95", AnalysisConclusion.Detected, FindingSeverity.Warning,
            900, 200, 3.5, "ms", "window_comparison", 10, anomalyAt,
            [anomalyEvidence.Id]);
        var engine = new CorrelationEngine(
            new FakeAnomalyEngine(new AnalysisResult<AnomalyReport>(
                new AnomalyReport(AnalysisConclusion.Detected, [anomaly], now),
                [Status("metrics")], [anomalyEvidence], [])),
            new FakeKubernetesProvider(
                new KubernetesWorkloadState(
                    "orders", 2, 1, 1, "4", "orders:2", null,
                    new Dictionary<string, string> { ["app"] = "orders" },
                    [new KubernetesPodState(
                        "orders-1", "Running", false, 3, false, true, false,
                        ["waiting:CrashLoopBackOff"], new Dictionary<string, string>(),
                        new Dictionary<string, string>())],
                    3, false, true),
                [new KubernetesEventRecord(
                    "event-1", now.AddMinutes(-9), "Warning", "BackOff",
                    "Back-off restarting container", "Pod", "orders-1", 3)]),
            new FakeTraceProvider([
                new NormalizedSpan(
                    "0123456789abcdef0123456789abcdef", "span-1", null,
                    "Orders", "POST /orders", now.AddMinutes(-8), 1200,
                    "ERROR", true, "payments", new Dictionary<string, string>(), [], false)
            ]),
            new UnavailableLogsProvider(),
            new FakeDeploymentProvider([
                new DeploymentChange(
                    "deploy-1", now.AddMinutes(-12), "version_change",
                    "Deployment changed to orders:2.", "4", "orders:2", [])
            ]),
            Options.Create(new ObservabilityLimitsOptions()));

        var result = await engine.BuildTimelineAsync(
            Service, new Dictionary<string, string> { ["app"] = "orders" }, window);

        Assert.Equal(AnalysisConclusion.Detected, result.Data.AnalysisConclusion);
        Assert.Equal(anomalyAt, result.Data.IncidentStartedAt);
        Assert.Equal(
            result.Data.Events.OrderBy(item => item.Timestamp).Select(item => item.Id),
            result.Data.Events.Select(item => item.Id));
        Assert.Contains(result.Data.Correlations,
            item => item.StartsWith("deployment_preceded_incident_by_", StringComparison.Ordinal));
        Assert.Contains(result.Sources,
            source => source.Name == "logs" && source.Availability == SourceAvailability.Unavailable);
        Assert.Contains(result.Data.Events, item => item.Type == "trace_errors");
        Assert.Contains(result.Data.Events, item => item.Type == "crash_loop");
        Assert.All(result.Data.Events.SelectMany(item => item.EvidenceIds),
            id => Assert.Contains(result.Evidence, evidence => evidence.Id == id));
    }

    private static SourceStatus Status(string name) =>
        new(name, SourceAvailability.Available, DateTimeOffset.UtcNow, 0, 1, []);

    private sealed class RecordingHandler(string payload) : HttpMessageHandler
    {
        public Uri? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeAnomalyEngine(AnalysisResult<AnomalyReport> result) : IAnomalyEngine
    {
        public Task<AnalysisResult<AnomalyReport>> DetectAsync(
            ServiceIdentity service,
            TimeWindow currentWindow,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeKubernetesProvider(
        KubernetesWorkloadState workload,
        IReadOnlyList<KubernetesEventRecord> events) : IKubernetesProvider
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
            Task.FromResult(ProviderResult<IReadOnlyList<KubernetesEventRecord>>.Available(
                "events", events, DateTimeOffset.UtcNow, 1));
    }

    private sealed class FakeTraceProvider(IReadOnlyList<NormalizedSpan> spans) : ITraceProvider
    {
        public Task<ProviderResult<IReadOnlyList<NormalizedSpan>>> GetSpansAsync(
            ServiceIdentity service,
            TimeWindow window,
            int maxTraces,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<NormalizedSpan>>.Available(
                "traces", spans, DateTimeOffset.UtcNow, 1));

        public Task<ProviderResult<IReadOnlyList<DependencyObservation>>> GetDependenciesAsync(
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnavailableLogsProvider : ILogsProvider
    {
        public Task<ProviderResult<IReadOnlyList<LogPattern>>> GetErrorPatternsAsync(
            ServiceIdentity service,
            TimeWindow window,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<LogPattern>>.Unavailable(
                "logs", 1, "fixture unavailable"));

        public Task<ProviderResult<IReadOnlyList<LogPattern>>> FindByTraceIdAsync(
            ServiceIdentity service,
            string traceId,
            TimeWindow window,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDeploymentProvider(IReadOnlyList<DeploymentChange> changes)
        : IDeploymentEventProvider
    {
        public Task<ProviderResult<IReadOnlyList<DeploymentChange>>> GetChangesAsync(
            ServiceIdentity service,
            TimeWindow window,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderResult<IReadOnlyList<DeploymentChange>>.Available(
                "deployments", changes, DateTimeOffset.UtcNow, 1));
    }
}
