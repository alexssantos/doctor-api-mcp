using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Anomalies;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;

namespace McpApis.McpServer.Engines.Correlation;

public interface ICorrelationEngine
{
    Task<AnalysisResult<IncidentTimeline>> BuildTimelineAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        TimeWindow window,
        CancellationToken cancellationToken = default);
}

public sealed partial class CorrelationEngine(
    IAnomalyEngine anomalyEngine,
    IKubernetesProvider kubernetesProvider,
    ITraceProvider traceProvider,
    ILogsProvider logsProvider,
    IDeploymentEventProvider deploymentProvider,
    IOptions<ObservabilityLimitsOptions> limits) : ICorrelationEngine
{
    public async Task<AnalysisResult<IncidentTimeline>> BuildTimelineAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("engine.correlation");
        activity?.SetTag("engine", "correlation");
        var anomalyTask = anomalyEngine.DetectAsync(service, window, cancellationToken);
        var eventTask = kubernetesProvider.GetEventsAsync(service, window, cancellationToken);
        var workloadTask = kubernetesProvider.GetWorkloadStateAsync(service, selector, cancellationToken);
        var traceTask = traceProvider.GetSpansAsync(
            service, window, limits.Value.MaxTraces, cancellationToken);
        var logTask = logsProvider.GetErrorPatternsAsync(
            service, window, limits.Value.MaxLogs, cancellationToken);
        var deploymentTask = deploymentProvider.GetChangesAsync(service, window, cancellationToken);
        await Task.WhenAll(
            anomalyTask, eventTask, workloadTask, traceTask, logTask, deploymentTask);

        var anomalies = await anomalyTask;
        var kubernetesEvents = await eventTask;
        var workload = await workloadTask;
        var traces = await traceTask;
        var logs = await logTask;
        var deployments = await deploymentTask;
        var evidence = new List<Evidence>(anomalies.Evidence);
        var events = new List<IncidentEvent>();

        AddDeployments(service, deployments.Value ?? [], evidence, events);
        AddAnomalies(service, anomalies.Data, window, events);
        AddKubernetesEvents(service, kubernetesEvents.Value ?? [], evidence, events);
        AddWorkloadState(service, workload, window, evidence, events);
        AddTraces(service, traces.Value ?? [], evidence, events);
        AddLogs(service, logs.Value ?? [], evidence, events);

        var deduplicated = Deduplicate(events);
        var issueEvents = deduplicated
            .Where(item => item.Severity is FindingSeverity.Warning or FindingSeverity.Critical)
            .ToArray();
        var incidentStart = issueEvents.Select(item => (DateTimeOffset?)item.Timestamp).Min();
        var correlations = BuildCorrelations(
            deduplicated, traces.Value ?? [], logs.Value ?? [], incidentStart);
        var sources = anomalies.Sources
            .Concat([
                kubernetesEvents.ToSourceStatus(),
                workload.ToSourceStatus(),
                traces.ToSourceStatus(),
                logs.ToSourceStatus(),
                deployments.ToSourceStatus()
            ])
            .ToArray();
        var anyConclusiveSource = sources.Any(source =>
            source.Availability != SourceAvailability.Unavailable);
        var conclusion = issueEvents.Length > 0
            ? AnalysisConclusion.Detected
            : anyConclusiveSource && anomalies.Data.AnalysisConclusion != AnalysisConclusion.Inconclusive
                ? AnalysisConclusion.NotDetected
                : AnalysisConclusion.Inconclusive;

        ObservabilityTelemetry.ProcessedItems.Add(deduplicated.Count,
            new KeyValuePair<string, object?>("item.type", "incident_events"));
        var warnings = anomalies.Warnings
            .Concat(kubernetesEvents.Warnings)
            .Concat(workload.Warnings)
            .Concat(traces.Warnings)
            .Concat(logs.Warnings)
            .Concat(deployments.Warnings)
            .Distinct()
            .ToArray();
        var timeline = new IncidentTimeline(
            conclusion, incidentStart, deduplicated, correlations);
        return new AnalysisResult<IncidentTimeline>(timeline, sources, evidence, warnings);
    }

    private static void AddDeployments(
        ServiceIdentity service,
        IReadOnlyList<DeploymentChange> changes,
        List<Evidence> evidence,
        List<IncidentEvent> events)
    {
        foreach (var change in changes)
        {
            var evidenceId = AddEvidence(
                evidence, $"deployments:{SafeId(change.Id)}", "deployments",
                change.Type, null, null, null, change.Timestamp,
                "catalog_snapshot+kubernetes_events", change.Summary);
            events.Add(new IncidentEvent(
                change.Id,
                change.Timestamp,
                change.Type,
                service,
                FindingSeverity.Info,
                "deployments",
                change.Summary,
                change.EvidenceIds.Append(evidenceId).Distinct().ToArray()));
        }
    }

    private static void AddAnomalies(
        ServiceIdentity service,
        AnomalyReport report,
        TimeWindow window,
        List<IncidentEvent> events)
    {
        foreach (var anomaly in report.Anomalies.Where(item =>
                     item.Conclusion == AnalysisConclusion.Detected))
        {
            var timestamp = anomaly.EstimatedStart ?? window.To;
            var deviation = anomaly.Deviation is null
                ? "outside its baseline"
                : $"{anomaly.Deviation.Value:+0.##%;-0.##%;0%} from baseline";
            events.Add(new IncidentEvent(
                EventId("anomaly", anomaly.Metric, timestamp),
                timestamp,
                $"{anomaly.Metric}_anomaly",
                service,
                anomaly.Severity,
                "metrics",
                $"{anomaly.Metric} is {deviation} ({anomaly.Method}, {anomaly.SampleCount} samples).",
                anomaly.EvidenceIds));
        }
    }

    private static void AddKubernetesEvents(
        ServiceIdentity service,
        IReadOnlyList<KubernetesEventRecord> items,
        List<Evidence> evidence,
        List<IncidentEvent> events)
    {
        foreach (var item in items)
        {
            var type = KubernetesEventType(item);
            var severity = item.Type.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                ? FindingSeverity.Warning
                : type is "oom_killed" or "crash_loop"
                    ? FindingSeverity.Critical
                    : FindingSeverity.Info;
            var evidenceId = AddEvidence(
                evidence, $"events:{SafeId(item.Id)}", "events", type,
                item.Count, null, "count", item.Timestamp,
                "kubernetes_event", item.Message);
            events.Add(new IncidentEvent(
                $"event:{item.Id}", item.Timestamp, type, service, severity, "events",
                $"{item.Reason} on {item.InvolvedKind}/{item.InvolvedName}: {item.Message}",
                [evidenceId]));
        }
    }

    private static void AddWorkloadState(
        ServiceIdentity service,
        ProviderResult<KubernetesWorkloadState> workload,
        TimeWindow window,
        List<Evidence> evidence,
        List<IncidentEvent> events)
    {
        if (workload.Value is not { } state || state.RestartCount <= 0 && state.HasPods)
            return;

        var timestamp = workload.ObservedAt ?? window.To;
        var oom = state.Pods.Any(pod => pod.OomKilled);
        var crash = state.Pods.Any(pod => pod.CrashLoopBackOff);
        var type = !state.HasPods ? "no_pods" : oom ? "oom_killed" : crash ? "crash_loop" : "pod_restarts";
        var severity = !state.HasPods || oom || crash || state.RestartCount >= 5
            ? FindingSeverity.Critical
            : FindingSeverity.Warning;
        var value = state.HasPods ? state.RestartCount : 0;
        var evidenceId = AddEvidence(
            evidence, $"kubernetes:{type}:snapshot", "kubernetes", type,
            value, 0, "count", timestamp, "pod_container_status",
            $"{state.ReadyReplicas}/{state.DesiredReplicas} replicas ready.");
        events.Add(new IncidentEvent(
            EventId("kubernetes", type, timestamp), timestamp, type, service,
            severity, "kubernetes",
            type switch
            {
                "no_pods" => "The workload has no matching Pods.",
                "oom_killed" => $"OOMKilled was observed; containers report {state.RestartCount} restart(s).",
                "crash_loop" => $"CrashLoopBackOff was observed; containers report {state.RestartCount} restart(s).",
                _ => $"Containers report {state.RestartCount} restart(s)."
            },
            [evidenceId]));
    }

    private static void AddTraces(
        ServiceIdentity service,
        IReadOnlyList<NormalizedSpan> spans,
        List<Evidence> evidence,
        List<IncidentEvent> events)
    {
        var significant = spans
            .Where(span => span.HasError || span.DurationMilliseconds >= 1000)
            .GroupBy(span => new
            {
                Type = span.HasError ? "trace_errors" : "slow_traces",
                span.OperationName,
                span.PeerService
            });
        foreach (var group in significant)
        {
            var first = group.Min(span => span.StartedAt);
            var count = group.Count();
            var evidenceId = AddEvidence(
                evidence,
                EventId("traces", $"{group.Key.Type}:{group.Key.OperationName}", first),
                "traces",
                group.Key.Type,
                count,
                null,
                "spans",
                first,
                "jaeger_normalized_spans",
                string.Join(',', group.Select(span => span.TraceId).Distinct().Take(5)));
            var peer = string.IsNullOrWhiteSpace(group.Key.PeerService)
                ? string.Empty
                : $" calling {group.Key.PeerService}";
            events.Add(new IncidentEvent(
                EventId("traces", $"{group.Key.Type}:{group.Key.OperationName}", first),
                first,
                group.Key.Type,
                service,
                group.Key.Type == "trace_errors" ? FindingSeverity.Warning : FindingSeverity.Warning,
                "traces",
                $"{count} {group.Key.Type.Replace('_', ' ')} in {group.Key.OperationName}{peer}.",
                [evidenceId]));
        }
    }

    private static void AddLogs(
        ServiceIdentity service,
        IReadOnlyList<LogPattern> patterns,
        List<Evidence> evidence,
        List<IncidentEvent> events)
    {
        foreach (var pattern in patterns)
        {
            var evidenceId = AddEvidence(
                evidence, $"logs:{pattern.Fingerprint}", "logs", "error_pattern",
                pattern.Count, null, "entries", pattern.FirstSeen,
                "loki_internal_template", pattern.Message);
            var severity = pattern.Level is "fatal" or "critical"
                ? FindingSeverity.Critical
                : FindingSeverity.Warning;
            events.Add(new IncidentEvent(
                $"log:{pattern.Fingerprint}", pattern.FirstSeen, "log_error_pattern",
                service, severity, "logs",
                $"{pattern.Count} occurrence(s) of log pattern {pattern.Fingerprint}: {pattern.Message}",
                [evidenceId]));
        }
    }

    private static IReadOnlyList<IncidentEvent> Deduplicate(IReadOnlyList<IncidentEvent> events) =>
        events
            .GroupBy(item => $"{item.Type}|{item.Source}|{Normalize(item.Summary)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.OrderBy(item => item.Timestamp).First();
                return first with
                {
                    Severity = group.Max(item => item.Severity),
                    EvidenceIds = group.SelectMany(item => item.EvidenceIds).Distinct().ToArray()
                };
            })
            .OrderBy(item => item.Timestamp)
            .ThenByDescending(item => item.Severity)
            .ToArray();

    private static IReadOnlyList<string> BuildCorrelations(
        IReadOnlyList<IncidentEvent> events,
        IReadOnlyList<NormalizedSpan> spans,
        IReadOnlyList<LogPattern> logs,
        DateTimeOffset? incidentStart)
    {
        var correlations = new List<string>();
        if (incidentStart is { } started)
        {
            foreach (var deployment in events.Where(item =>
                         item.Source == "deployments" && item.Timestamp <= started &&
                         started - item.Timestamp <= TimeSpan.FromMinutes(30)))
            {
                correlations.Add(
                    $"deployment_preceded_incident_by_{Math.Max(0, (long)(started - deployment.Timestamp).TotalSeconds)}s:{deployment.Id}");
            }

            foreach (var restart in events.Where(item =>
                         item.Type is "pod_restarts" or "oom_killed" or "crash_loop" &&
                         Math.Abs((item.Timestamp - started).TotalMinutes) <= 15))
            {
                correlations.Add($"workload_instability_near_incident:{restart.Id}");
            }
        }

        var traceIds = spans.Select(span => span.TraceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in logs.Where(pattern =>
                     pattern.TraceId is not null && traceIds.Contains(pattern.TraceId)))
            correlations.Add($"trace_log_match:{pattern.TraceId}:{pattern.Fingerprint}");

        if (events.Any(item => item.Type.EndsWith("_anomaly", StringComparison.Ordinal)) &&
            events.Any(item => item.Type == "trace_errors"))
            correlations.Add("metric_anomaly_overlaps_trace_errors");

        return correlations.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string AddEvidence(
        List<Evidence> evidence,
        string requestedId,
        string source,
        string signal,
        double? value,
        double? baseline,
        string? unit,
        DateTimeOffset timestamp,
        string descriptor,
        string? detail)
    {
        var id = requestedId;
        var suffix = 1;
        while (evidence.Any(item => item.Id == id))
            id = $"{requestedId}:{++suffix}";
        evidence.Add(new Evidence(
            id, source, signal, value, baseline, unit, timestamp, descriptor, detail));
        return id;
    }

    private static string KubernetesEventType(KubernetesEventRecord item)
    {
        if (item.Reason.Contains("OOM", StringComparison.OrdinalIgnoreCase) ||
            item.Message.Contains("OOMKilled", StringComparison.OrdinalIgnoreCase))
            return "oom_killed";
        if (item.Reason.Contains("BackOff", StringComparison.OrdinalIgnoreCase) ||
            item.Message.Contains("CrashLoop", StringComparison.OrdinalIgnoreCase))
            return "crash_loop";
        if (item.Reason.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase))
            return "probe_failure";
        if (item.Reason.Contains("Scaling", StringComparison.OrdinalIgnoreCase))
            return "scale_event";
        return "kubernetes_event";
    }

    private static string EventId(string source, string type, DateTimeOffset timestamp) =>
        $"{source}:{SafeId(type)}:{timestamp.ToUnixTimeMilliseconds()}";

    private static string SafeId(string value)
    {
        var normalized = NonIdRegex().Replace(value.ToLowerInvariant(), "-").Trim('-');
        if (normalized.Length <= 48)
            return normalized;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8]
            .ToLowerInvariant();
        return $"{normalized[..39]}-{hash}";
    }

    private static string Normalize(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex("[^a-zA-Z0-9._:-]+")]
    private static partial Regex NonIdRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
