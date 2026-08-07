using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using McpApis.BuildingBlocks.Observability;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Providers.Jaeger;

public sealed class JaegerTraceProvider(
    IJaegerCollector collector,
    IOptions<ObservabilityLimitsOptions> limits,
    ILogger<JaegerTraceProvider> logger) : ITraceProvider
{
    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http.request.method", "http.method", "http.route", "url.path",
        "http.response.status_code", "http.status_code", "otel.status_code",
        "rpc.system", "rpc.service", "rpc.method", "db.system", "db.namespace",
        "db.operation.name", "peer.service", "server.address", "network.peer.address"
    };

    private static readonly IReadOnlySet<string> SensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "authorization", "cookie", "apiKey", "connectionString"
    };

    public async Task<ProviderResult<IReadOnlyList<NormalizedSpan>>> GetSpansAsync(
        ServiceIdentity service,
        TimeWindow window,
        int maxTraces,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("provider.traces.spans");
        activity?.SetTag("provider", "jaeger");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.Value.ProviderTimeoutSeconds));
            var capped = Math.Clamp(maxTraces, 1, limits.Value.MaxTraces);
            var raw = await collector.GetTraceSpansAsync(
                service.OtelServiceName ?? service.ServiceName,
                limit: capped,
                start: window.From,
                end: window.To,
                cancellationToken: timeout.Token);

            var normalized = raw.Take(limits.Value.MaxSpans).Select(span =>
            {
                var attributes = span.Tags
                    .Where(kv => AllowedAttributes.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => SensitiveDataRedactor.Redact(kv.Value, SensitiveFields));
                var httpStatus = attributes.GetValueOrDefault("http.response.status_code") ??
                                 attributes.GetValueOrDefault("http.status_code");
                var error = span.Status.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
                            span.Tags.GetValueOrDefault("error") == "true" ||
                            (int.TryParse(httpStatus, out var status) && status >= 500);
                var peer = attributes.GetValueOrDefault("peer.service") ??
                           attributes.GetValueOrDefault("server.address");
                return new NormalizedSpan(
                    span.TraceId,
                    span.SpanId,
                    span.ParentSpanId,
                    span.ServiceName,
                    span.OperationName,
                    span.StartedAt,
                    span.Duration / 1000d,
                    span.Status,
                    error,
                    peer,
                    attributes,
                    span.Events.Select(e => SensitiveDataRedactor.Redact(e, SensitiveFields)).ToArray(),
                    span.Tags.Count != attributes.Count);
            }).ToArray();

            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "available");
            var warnings = raw.Count > limits.Value.MaxSpans
                ? new[] { $"Span result truncated to {limits.Value.MaxSpans} items." }
                : [];
            return ProviderResult<IReadOnlyList<NormalizedSpan>>.Available(
                "traces", normalized, DateTimeOffset.UtcNow, (long)elapsed.TotalMilliseconds, warnings);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "timeout");
            return ProviderResult<IReadOnlyList<NormalizedSpan>>.Unavailable(
                "traces", (long)elapsed.TotalMilliseconds, "Jaeger provider timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Jaeger trace collection failed.");
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "unavailable");
            return ProviderResult<IReadOnlyList<NormalizedSpan>>.Unavailable(
                "traces", (long)elapsed.TotalMilliseconds, Describe(ex));
        }
    }

    public async Task<ProviderResult<IReadOnlyList<DependencyObservation>>> GetDependenciesAsync(
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("provider.traces.dependencies");
        activity?.SetTag("provider", "jaeger");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.Value.ProviderTimeoutSeconds));
            var raw = await collector.GetDependenciesAsync(
                Math.Max(1, (long)window.Span.TotalMilliseconds), timeout.Token);
            var observations = new List<DependencyObservation>();
            if (raw.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var edge in data.EnumerateArray().Take(limits.Value.MaxDependencies))
                {
                    var parent = edge.TryGetProperty("parent", out var p) ? p.GetString() : null;
                    var child = edge.TryGetProperty("child", out var c) ? c.GetString() : null;
                    if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(child))
                        continue;
                    observations.Add(new DependencyObservation(
                        parent,
                        child,
                        DateTimeOffset.UtcNow,
                        edge.TryGetProperty("callCount", out var count) ? count.GetInt64() : 0,
                        0,
                        null,
                        "jaeger_dependency_graph",
                        []));
                }
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "available");
            return ProviderResult<IReadOnlyList<DependencyObservation>>.Available(
                "traces", observations, DateTimeOffset.UtcNow, (long)elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "timeout");
            return ProviderResult<IReadOnlyList<DependencyObservation>>.Unavailable(
                "traces", (long)elapsed.TotalMilliseconds, "Jaeger provider timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Jaeger dependency collection failed.");
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "unavailable");
            return ProviderResult<IReadOnlyList<DependencyObservation>>.Unavailable(
                "traces", (long)elapsed.TotalMilliseconds, Describe(ex));
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException => "Jaeger request failed.",
        JsonException => "Jaeger returned malformed JSON.",
        _ => ex.Message
    };

    private static void Record(TimeSpan elapsed, string availability)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("provider", "jaeger"),
            new KeyValuePair<string, object?>("availability", availability)
        };
        ObservabilityTelemetry.ProviderCalls.Add(1, tags);
        ObservabilityTelemetry.ProviderDuration.Record(elapsed.TotalMilliseconds, tags);
    }
}
