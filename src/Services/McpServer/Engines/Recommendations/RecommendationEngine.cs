using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;

namespace McpApis.McpServer.Engines.Recommendations;

public interface IRecommendationEngine
{
    IReadOnlyList<Recommendation> Generate(
        ServiceIdentity service,
        IncidentTimeline timeline,
        HealthReport health,
        DependencyGraph dependencies,
        RootCauseHypothesis? primaryHypothesis,
        IReadOnlyList<string> limitations);
}

/// <summary>
/// Versioned, deterministic recommendation rules. These rules only suggest
/// investigation steps; no recommendation is executable by design.
/// </summary>
public sealed class RecommendationEngine : IRecommendationEngine
{
    public IReadOnlyList<Recommendation> Generate(
        ServiceIdentity service,
        IncidentTimeline timeline,
        HealthReport health,
        DependencyGraph dependencies,
        RootCauseHypothesis? primaryHypothesis,
        IReadOnlyList<string> limitations)
    {
        var recommendations = new List<Recommendation>();

        var deployment = timeline.Events.LastOrDefault(item =>
            item.Source == "deployments" &&
            (timeline.IncidentStartedAt is null || item.Timestamp <= timeline.IncidentStartedAt));
        if (deployment is not null)
        {
            Add(recommendations, "P1",
                "Compare the current deployment revision and image with the preceding known-good version; assess rollback safety without executing a change.",
                "A deployment or scaling change occurred close to the incident window.",
                deployment.EvidenceIds);
        }

        var oom = timeline.Events.FirstOrDefault(item => item.Type == "oom_killed");
        if (oom is not null)
        {
            Add(recommendations, "P1",
                "Inspect container memory working set, requests, limits and the OOM termination record.",
                "OOMKilled evidence is present in the correlated timeline.",
                oom.EvidenceIds);
        }

        var crash = timeline.Events.FirstOrDefault(item =>
            item.Type is "crash_loop" or "pod_restarts");
        if (crash is not null)
        {
            Add(recommendations, "P1",
                "Inspect the affected Pod termination state and its redacted error-log fingerprint before considering any restart.",
                "Workload instability overlaps the incident window.",
                crash.EvidenceIds);
        }

        var dependency = dependencies.Outbound
            .OrderByDescending(edge => edge.ErrorRate ?? 0)
            .ThenByDescending(edge => edge.LatencyMilliseconds ?? 0)
            .FirstOrDefault(edge =>
                primaryHypothesis?.PotentiallyAffectedServices.Contains(
                    edge.Target.Key, StringComparer.OrdinalIgnoreCase) == true ||
                (edge.ErrorRate ?? 0) > 0 || (edge.LatencyMilliseconds ?? 0) >= 500);
        if (dependency is not null)
        {
            Add(recommendations, "P1",
                $"Inspect health, error traces and recent changes for downstream dependency {dependency.Target.Key}.",
                "Observed dependency evidence overlaps the degraded service path.",
                dependency.EvidenceIds);
        }

        var traceError = timeline.Events.FirstOrDefault(item => item.Type == "trace_errors");
        var logError = timeline.Events.FirstOrDefault(item => item.Type == "log_error_pattern");
        if (traceError is not null || logError is not null)
        {
            Add(recommendations, "P2",
                "Compare the dominant error trace operation with the matching redacted log fingerprint and status-code evidence.",
                "Trace or log error evidence is available for the incident window.",
                (traceError?.EvidenceIds ?? []).Concat(logError?.EvidenceIds ?? []).ToArray());
        }

        var latency = timeline.Events.FirstOrDefault(item =>
            item.Type == "latency_p95_anomaly" || item.Type == "slow_traces");
        if (latency is not null)
        {
            Add(recommendations, "P2",
                "Inspect the slowest traced operation and compare its latency with the published baseline window.",
                "Latency anomaly or slow-span evidence is present.",
                latency.EvidenceIds);
        }

        var saturation = health.Findings.FirstOrDefault(item =>
            item.Type is "saturation" or "oom_killed");
        if (saturation is not null)
        {
            Add(recommendations, "P2",
                "Compare CPU and memory measurements with resource requests and limits; review HPA behavior if configured.",
                "The Health Engine reported saturation-related evidence.",
                saturation.EvidenceIds);
        }

        if (limitations.Count > 0)
        {
            Add(recommendations, "P3",
                "Restore or verify the unavailable telemetry sources before increasing diagnostic confidence.",
                "The RCA report lists incomplete or stale signal coverage.",
                []);
        }

        if (timeline.AnalysisConclusion == AnalysisConclusion.Detected && recommendations.Count == 0)
        {
            Add(recommendations, "P3",
                $"Collect a wider evidence window for {service.Key} and verify trace/log correlation identifiers.",
                "An incident was detected, but no more specific deterministic recommendation rule matched.",
                []);
        }

        return recommendations.Take(8).ToArray();
    }

    private static void Add(
        List<Recommendation> recommendations,
        string priority,
        string action,
        string reason,
        IReadOnlyList<string> evidenceIds)
    {
        if (recommendations.Any(item => item.Action == action))
            return;
        recommendations.Add(new Recommendation(
            priority, action, reason, evidenceIds.Distinct().ToArray(), Executable: false));
    }
}
