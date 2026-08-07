using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Providers.Prometheus;

public sealed class PrometheusMetricsProvider(
    IPrometheusCollector collector,
    IOptions<MetricsTemplateOptions> templates,
    IOptions<ObservabilityLimitsOptions> limits,
    ILogger<PrometheusMetricsProvider> logger) : IMetricsProvider
{
    public async Task<ProviderResult<RedMetrics>> GetRedMetricsAsync(
        ServiceIdentity service,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("provider.metrics.red");
        activity?.SetTag("provider", "prometheus");
        var warnings = new List<string>();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.Value.ProviderTimeoutSeconds));

            var queries = BuildQueries(service, window);
            var tasks = queries.Select(async pair =>
            {
                try
                {
                    var result = await collector.QueryAsync(pair.Value.Query, timeout.Token);
                    return (pair.Key, Measurement: ParseInstant(
                        result, pair.Value.Name, pair.Value.Unit, pair.Value.Aggregation));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    lock (warnings)
                        warnings.Add($"{pair.Value.Name}: {Describe(ex)}");
                    return (pair.Key, Measurement: (Measurement?)null);
                }
            });

            var values = (await Task.WhenAll(tasks)).ToDictionary(x => x.Key, x => x.Measurement);
            var p50 = await QueryPercentileAsync(service, window, 0.50, "latency_p50", timeout.Token, warnings);
            var metrics = new RedMetrics(
                values[MetricSignal.RequestRate],
                values[MetricSignal.ErrorRate],
                p50,
                values[MetricSignal.P95Latency],
                await QueryPercentileAsync(service, window, 0.99, "latency_p99", timeout.Token, warnings),
                values[MetricSignal.Availability],
                values[MetricSignal.CpuUsage],
                values[MetricSignal.MemoryUsage]);

            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "available");
            return ProviderResult<RedMetrics>.Available(
                "metrics", metrics, DateTimeOffset.UtcNow, (long)elapsed.TotalMilliseconds,
                warnings.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "timeout");
            return ProviderResult<RedMetrics>.Unavailable(
                "metrics", (long)elapsed.TotalMilliseconds, "Prometheus provider timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prometheus metrics collection failed.");
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "unavailable");
            return ProviderResult<RedMetrics>.Unavailable(
                "metrics", (long)elapsed.TotalMilliseconds, Describe(ex));
        }
    }

    public async Task<ProviderResult<MetricSeries>> GetSeriesAsync(
        ServiceIdentity service,
        MetricSignal signal,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("provider.metrics.series");
        activity?.SetTag("provider", "prometheus");
        activity?.SetTag("signal", signal.ToString());

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.Value.ProviderTimeoutSeconds));
            var descriptor = BuildQueries(service, window)[signal];
            var stepSeconds = Math.Max(
                limits.Value.MinimumRangeStepSeconds,
                (int)Math.Ceiling(window.Span.TotalSeconds / 240));
            var json = await collector.QueryRangeAsync(
                descriptor.Query, window.From, window.To, $"{stepSeconds}s", timeout.Token);
            var series = ParseSeries(json, descriptor.Name, descriptor.Unit, descriptor.Aggregation);
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "available");
            var warning = series.Points.Count == 0 ? new[] { $"No samples for {descriptor.Name}." } : [];
            return ProviderResult<MetricSeries>.Available(
                "metrics", series, DateTimeOffset.UtcNow, (long)elapsed.TotalMilliseconds, warning);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "timeout");
            return ProviderResult<MetricSeries>.Unavailable(
                "metrics", (long)elapsed.TotalMilliseconds, "Prometheus provider timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prometheus range collection failed for {Signal}.", signal);
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "unavailable");
            return ProviderResult<MetricSeries>.Unavailable(
                "metrics", (long)elapsed.TotalMilliseconds, Describe(ex));
        }
    }

    private Dictionary<MetricSignal, QueryDescriptor> BuildQueries(ServiceIdentity service, TimeWindow window)
    {
        var options = templates.Value;
        var id = EscapeLabelValue(service.MetricsId ?? service.ServiceName);
        var selector = $"{options.ServiceLabel}=\"{id}\"";
        var range = $"{Math.Max(60, (int)window.Span.TotalSeconds)}s";
        var count = options.RequestDurationCountMetric;
        var bucket = options.RequestDurationBucketMetric;
        var status = options.StatusCodeLabel;

        var requestRate = $"sum(rate({count}{{{selector}}}[{range}]))";
        var errorRate =
            $"sum(rate({count}{{{selector},{status}=~\"4..|5..\"}}[{range}])) / " +
            $"clamp_min({requestRate}, 0.000000001)";
        var p95 = $"histogram_quantile(0.95, sum by (le) (rate({bucket}{{{selector}}}[{range}]))) * 1000";

        return new Dictionary<MetricSignal, QueryDescriptor>
        {
            [MetricSignal.RequestRate] = new(requestRate, "request_rate", "requests/s", "rate"),
            [MetricSignal.ErrorRate] = new(errorRate, "error_rate", "ratio", "rate"),
            [MetricSignal.P95Latency] = new(p95, "latency_p95", "ms", "p95"),
            [MetricSignal.Availability] = new(
                $"min({options.AvailabilityMetric}{{{selector}}})", "availability", "ratio", "minimum"),
            [MetricSignal.CpuUsage] = new(
                $"sum(rate({options.CpuMetric}{{{selector}}}[{range}]))", "cpu_usage", "cores", "rate"),
            [MetricSignal.MemoryUsage] = new(
                $"sum({options.MemoryMetric}{{{selector}}})", "memory_usage", "bytes", "sum")
        };
    }

    private async Task<Measurement?> QueryPercentileAsync(
        ServiceIdentity service,
        TimeWindow window,
        double percentile,
        string name,
        CancellationToken cancellationToken,
        List<string> warnings)
    {
        try
        {
            var options = templates.Value;
            var id = EscapeLabelValue(service.MetricsId ?? service.ServiceName);
            var range = $"{Math.Max(60, (int)window.Span.TotalSeconds)}s";
            var query =
                $"histogram_quantile({percentile.ToString(CultureInfo.InvariantCulture)}, sum by (le) (rate({options.RequestDurationBucketMetric}" +
                $"{{{options.ServiceLabel}=\"{id}\"}}[{range}]))) * 1000";
            var result = await collector.QueryAsync(query, cancellationToken);
            return ParseInstant(result, name, "ms", name.Replace("latency_", string.Empty));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"{name}: {Describe(ex)}");
            return null;
        }
    }

    internal static Measurement? ParseInstant(
        JsonElement root,
        string name,
        string unit,
        string aggregation)
    {
        if (!TryGetResults(root, out var results) || results.GetArrayLength() == 0)
            return null;
        var first = results[0];
        if (!first.TryGetProperty("value", out var value) || value.GetArrayLength() < 2 ||
            !TryParseDouble(value[1], out var parsed))
            return null;
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(value[0].GetDouble() * 1000));
        return new Measurement(name, parsed, unit, timestamp, aggregation);
    }

    internal static MetricSeries ParseSeries(
        JsonElement root,
        string name,
        string unit,
        string aggregation)
    {
        var points = new SortedDictionary<DateTimeOffset, double>();
        if (TryGetResults(root, out var results))
        {
            foreach (var result in results.EnumerateArray())
            {
                if (!result.TryGetProperty("values", out var values))
                    continue;
                foreach (var sample in values.EnumerateArray())
                {
                    if (sample.GetArrayLength() < 2 || !TryParseDouble(sample[1], out var parsed))
                        continue;
                    var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(
                        (long)(sample[0].GetDouble() * 1000));
                    points[timestamp] = points.GetValueOrDefault(timestamp) + parsed;
                }
            }
        }
        return new MetricSeries(name, unit, aggregation,
            points.Select(p => new MetricPoint(p.Key, p.Value)).ToArray());
    }

    private static bool TryGetResults(JsonElement root, out JsonElement results)
    {
        results = default;
        return root.TryGetProperty("status", out var status) && status.GetString() == "success" &&
               root.TryGetProperty("data", out var data) &&
               data.TryGetProperty("result", out results) && results.ValueKind == JsonValueKind.Array;
    }

    private static bool TryParseDouble(JsonElement value, out double parsed) =>
        double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
        double.IsFinite(parsed);

    private static string EscapeLabelValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => "query timed out",
        HttpRequestException => "Prometheus request failed",
        JsonException => "Prometheus returned malformed JSON",
        _ => ex.Message
    };

    private static void Record(TimeSpan elapsed, string availability)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("provider", "prometheus"),
            new KeyValuePair<string, object?>("availability", availability)
        };
        ObservabilityTelemetry.ProviderCalls.Add(1, tags);
        ObservabilityTelemetry.ProviderDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    private sealed record QueryDescriptor(string Query, string Name, string Unit, string Aggregation);
}
