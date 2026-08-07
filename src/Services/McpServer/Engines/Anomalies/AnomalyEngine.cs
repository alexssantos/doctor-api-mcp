using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;

namespace McpApis.McpServer.Engines.Anomalies;

public interface IAnomalyEngine
{
    Task<AnalysisResult<AnomalyReport>> DetectAsync(
        ServiceIdentity service,
        TimeWindow currentWindow,
        CancellationToken cancellationToken = default);
}

public sealed class AnomalyEngine(IMetricsProvider metricsProvider) : IAnomalyEngine
{
    private static readonly MetricSignal[] Signals =
    [
        MetricSignal.RequestRate,
        MetricSignal.ErrorRate,
        MetricSignal.P95Latency,
        MetricSignal.CpuUsage,
        MetricSignal.MemoryUsage
    ];

    public async Task<AnalysisResult<AnomalyReport>> DetectAsync(
        ServiceIdentity service,
        TimeWindow currentWindow,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("engine.anomalies");
        activity?.SetTag("engine", "anomalies");
        var windows = ComparisonWindows(currentWindow);
        var tasks = Signals.SelectMany(signal => windows.Select(async pair =>
        {
            var result = await metricsProvider.GetSeriesAsync(
                service, signal, pair.Value, cancellationToken);
            return (Signal: signal, Window: pair.Key, Result: result);
        })).ToArray();
        var collected = await Task.WhenAll(tasks);

        var evidence = new List<Evidence>();
        var anomalies = new List<Anomaly>();
        var inconclusive = 0;
        foreach (var signal in Signals)
        {
            var series = collected.Where(item => item.Signal == signal)
                .ToDictionary(item => item.Window, item => item.Result);
            var current = series["current"].Value;
            var baselinePoints = new[] { "previous", "day", "week" }
                .SelectMany(name => series[name].Value?.Points ?? [])
                .Select(point => point.Value)
                .Where(double.IsFinite)
                .ToArray();
            var currentPoints = current?.Points.Select(point => point.Value)
                .Where(double.IsFinite).ToArray() ?? [];
            if (currentPoints.Length < 3 || baselinePoints.Length < 3)
            {
                inconclusive++;
                anomalies.Add(new Anomaly(
                    SignalName(signal), AnalysisConclusion.Inconclusive,
                    FindingSeverity.Info,
                    MeanOrNull(currentPoints), MeanOrNull(baselinePoints), null,
                    current?.Unit ?? Unit(signal), "window_comparison", currentPoints.Length,
                    null, []));
                continue;
            }

            var currentValue = currentPoints.Average();
            var expected = Median(baselinePoints);
            var mad = Median(baselinePoints.Select(value => Math.Abs(value - expected)).ToArray());
            var robustZ = mad > 1e-12 ? 0.6745 * (currentValue - expected) / mad : 0;
            var relative = Math.Abs(expected) > 1e-12
                ? (currentValue - expected) / Math.Abs(expected)
                : currentValue == 0 ? 0 : Math.Sign(currentValue);
            var detected = IsDetected(signal, currentValue, expected, relative, robustZ);
            var currentEvidence = AddEvidence(
                evidence, signal, "current", currentValue, expected, current!.Unit,
                currentWindow.To, "prometheus_template:current_window");
            var baselineEvidence = AddEvidence(
                evidence, signal, "baseline", expected, null, current.Unit,
                currentWindow.From, "comparison:previous+24h+7d");

            if (!detected)
                continue;

            var severity = Severity(signal, relative, robustZ);
            anomalies.Add(new Anomaly(
                SignalName(signal), AnalysisConclusion.Detected, severity,
                currentValue, expected, relative, current.Unit,
                mad > 1e-12 ? "robust_zscore+window_comparison" : "window_comparison",
                currentPoints.Length,
                EstimateStart(current.Points, expected, signal),
                [currentEvidence, baselineEvidence]));
        }

        var conclusion = anomalies.Any(a => a.Conclusion == AnalysisConclusion.Detected)
            ? AnalysisConclusion.Detected
            : inconclusive == Signals.Length
                ? AnalysisConclusion.Inconclusive
                : AnalysisConclusion.NotDetected;
        var report = new AnomalyReport(conclusion, anomalies, DateTimeOffset.UtcNow);
        var warnings = collected.SelectMany(item => item.Result.Warnings).Distinct().ToArray();
        var statuses = collected.Select(item => item.Result.ToSourceStatus()).ToArray();
        var availability = statuses.All(s => s.Availability == SourceAvailability.Available)
            ? SourceAvailability.Available
            : statuses.Any(s => s.Availability != SourceAvailability.Unavailable)
                ? SourceAvailability.Stale
                : SourceAvailability.Unavailable;
        var source = new SourceStatus(
            "metrics", availability,
            statuses.Select(s => s.ObservedAt).DefaultIfEmpty().Max(),
            statuses.Select(s => s.FreshnessSeconds).DefaultIfEmpty().Max(),
            statuses.Sum(s => s.ElapsedMilliseconds), warnings);
        return new AnalysisResult<AnomalyReport>(report, [source], evidence, warnings);
    }

    private static Dictionary<string, TimeWindow> ComparisonWindows(TimeWindow current)
    {
        var duration = current.Span;
        return new Dictionary<string, TimeWindow>
        {
            ["current"] = current,
            ["previous"] = new(current.From - duration, current.From, TimeWindow.Format(duration)),
            ["day"] = new(current.From.AddDays(-1), current.To.AddDays(-1), TimeWindow.Format(duration)),
            ["week"] = new(current.From.AddDays(-7), current.To.AddDays(-7), TimeWindow.Format(duration))
        };
    }

    private static bool IsDetected(
        MetricSignal signal,
        double current,
        double expected,
        double relative,
        double robustZ)
    {
        if (signal == MetricSignal.ErrorRate && current < 0.01)
            return false;
        if (signal == MetricSignal.RequestRate)
            return Math.Abs(relative) >= 0.50 || Math.Abs(robustZ) >= 3.5;
        return relative >= 0.35 || robustZ >= 3.5;
    }

    private static FindingSeverity Severity(MetricSignal signal, double relative, double robustZ)
    {
        if (signal == MetricSignal.RequestRate && relative > 0)
            return FindingSeverity.Info;
        var magnitude = Math.Max(Math.Abs(relative), Math.Abs(robustZ) / 3.5);
        return magnitude >= 2 ? FindingSeverity.Critical : FindingSeverity.Warning;
    }

    private static DateTimeOffset? EstimateStart(
        IReadOnlyList<MetricPoint> points,
        double expected,
        MetricSignal signal)
    {
        var threshold = signal == MetricSignal.RequestRate
            ? Math.Abs(expected) * 0.5
            : Math.Abs(expected) * 0.35;
        return points.FirstOrDefault(p => Math.Abs(p.Value - expected) >= threshold)?.Timestamp;
    }

    private static string AddEvidence(
        List<Evidence> evidence,
        MetricSignal signal,
        string kind,
        double value,
        double? baseline,
        string unit,
        DateTimeOffset timestamp,
        string descriptor)
    {
        var id = $"metrics:{SignalName(signal)}:{kind}:{evidence.Count + 1}";
        evidence.Add(new Evidence(
            id, "metrics", SignalName(signal), value, baseline, unit,
            timestamp, descriptor));
        return id;
    }

    private static double Median(double[] values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static double? MeanOrNull(double[] values) =>
        values.Length == 0 ? null : values.Average();

    private static string SignalName(MetricSignal signal) => signal switch
    {
        MetricSignal.RequestRate => "request_rate",
        MetricSignal.ErrorRate => "error_rate",
        MetricSignal.P95Latency => "latency_p95",
        MetricSignal.Availability => "availability",
        MetricSignal.CpuUsage => "cpu_usage",
        MetricSignal.MemoryUsage => "memory_usage",
        _ => signal.ToString().ToLowerInvariant()
    };

    private static string Unit(MetricSignal signal) => signal switch
    {
        MetricSignal.RequestRate => "requests/s",
        MetricSignal.ErrorRate or MetricSignal.Availability => "ratio",
        MetricSignal.P95Latency => "ms",
        MetricSignal.CpuUsage => "cores",
        MetricSignal.MemoryUsage => "bytes",
        _ => "value"
    };
}
