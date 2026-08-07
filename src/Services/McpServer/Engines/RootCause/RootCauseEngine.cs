using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Correlation;
using McpApis.McpServer.Engines.Dependencies;
using McpApis.McpServer.Engines.Health;
using McpApis.McpServer.Engines.Recommendations;
using McpApis.McpServer.Infrastructure.Telemetry;

namespace McpApis.McpServer.Engines.RootCause;

public interface IRootCauseEngine
{
    Task<AnalysisResult<RootCauseReport>> AnalyzeAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        TimeWindow window,
        int dependencyDepth,
        CancellationToken cancellationToken = default);
}

public sealed class RootCauseEngine(
    ICorrelationEngine correlationEngine,
    IHealthAnalysisService healthAnalysis,
    IDependencyEngine dependencyEngine,
    IRecommendationEngine recommendationEngine) : IRootCauseEngine
{
    private static readonly string[] ExpectedSources =
        ["metrics", "kubernetes", "events", "traces", "logs", "deployments"];

    public async Task<AnalysisResult<RootCauseReport>> AnalyzeAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        TimeWindow window,
        int dependencyDepth,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("engine.root_cause");
        activity?.SetTag("engine", "root_cause");
        var timelineTask = correlationEngine.BuildTimelineAsync(
            service, selector, window, cancellationToken);
        var healthTask = healthAnalysis.EvaluateAsync(
            service, selector, window, cancellationToken);
        var dependencyTask = dependencyEngine.AnalyzeAsync(
            service, window, dependencyDepth, cancellationToken);
        await Task.WhenAll(timelineTask, healthTask, dependencyTask);

        var timeline = await timelineTask;
        var health = await healthTask;
        var dependencies = await dependencyTask;
        var sources = timeline.Sources.Concat(health.Sources).Concat(dependencies.Sources).ToArray();
        var evidence = timeline.Evidence.Concat(health.Evidence).Concat(dependencies.Evidence)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var evidenceIds = evidence.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var coverage = CalculateCoverage(sources);
        var limitations = BuildLimitations(sources, timeline.Data, dependencies.Data);

        if (timeline.Data.AnalysisConclusion == AnalysisConclusion.NotDetected)
        {
            var noIncident = new RootCauseReport(
                AnalysisConclusion.NotDetected, null, [], coverage,
                limitations.Append("No incident-level signal was detected in the requested window.").ToArray(),
                []);
            return Result(noIncident, sources, evidence, timeline, health, dependencies);
        }

        var candidates = BuildCandidates(
            service, timeline.Data, health.Data, dependencies.Data, evidenceIds);
        var hypotheses = candidates
            .Select(candidate => ToHypothesis(candidate, coverage))
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var primary = hypotheses.FirstOrDefault(item =>
            item.Confidence >= 0.50 &&
            (item.SupportingEvidenceIds.Count >= 2 ||
             candidates.First(candidate => candidate.Id == item.Id).BaseConfidence >= 0.80));
        var conclusion = primary is not null
            ? AnalysisConclusion.Detected
            : AnalysisConclusion.Inconclusive;
        if (primary is null)
            limitations.Add("Available evidence did not cross the deterministic RCA confidence threshold.");
        var alternatives = hypotheses
            .Where(item => item.Id != primary?.Id)
            .Take(4)
            .ToArray();
        var recommendations = recommendationEngine.Generate(
            service, timeline.Data, health.Data, dependencies.Data, primary, limitations);
        var report = new RootCauseReport(
            conclusion, primary, alternatives, coverage, limitations, recommendations);
        return Result(report, sources, evidence, timeline, health, dependencies);
    }

    private static List<Candidate> BuildCandidates(
        ServiceIdentity service,
        IncidentTimeline timeline,
        HealthReport health,
        DependencyGraph dependencies,
        IReadOnlySet<string> evidenceIds)
    {
        var candidates = new List<Candidate>();
        var healthyEvidence = health.HealthStatus == HealthState.Healthy
            ? health.Dimensions.SelectMany(item => item.EvidenceIds).Where(evidenceIds.Contains).ToArray()
            : [];

        AddWorkloadCandidate("no_pods", "target_workload_unavailable",
            $"The workload for {service.Key} had no matching Pods.", 0.95);
        AddWorkloadCandidate("oom_killed", "target_memory_exhaustion",
            $"Memory exhaustion in {service.Key} is the strongest observed fault origin.", 0.90);
        AddWorkloadCandidate("crash_loop", "target_crash_loop",
            $"A CrashLoopBackOff in {service.Key} is the strongest observed fault origin.", 0.85);
        AddWorkloadCandidate("pod_restarts", "target_restart_instability",
            $"Container restart instability in {service.Key} plausibly caused the incident.", 0.65);

        var incidentStart = timeline.IncidentStartedAt;
        var deployment = timeline.Events
            .Where(item => item.Source == "deployments" &&
                           (incidentStart is null || item.Timestamp <= incidentStart))
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault();
        if (deployment is not null && incidentStart is { } started &&
            started - deployment.Timestamp <= TimeSpan.FromMinutes(30))
        {
            var following = timeline.Events.Where(item =>
                    item.Timestamp >= deployment.Timestamp &&
                    item.Severity is FindingSeverity.Warning or FindingSeverity.Critical)
                .SelectMany(item => item.EvidenceIds);
            var support = deployment.EvidenceIds.Concat(following)
                .Where(evidenceIds.Contains).Distinct().ToArray();
            var distance = Math.Max(0, (started - deployment.Timestamp).TotalSeconds);
            var temporalScore = 0.82 - Math.Min(0.17, distance / 1800d * 0.17);
            candidates.Add(new Candidate(
                "recent_deployment",
                $"A recent deployment or scale change in {service.Key} preceded the observed degradation.",
                temporalScore,
                support,
                healthyEvidence,
                dependencies.PotentialBlastRadius));
        }

        var traceEvents = timeline.Events.Where(item => item.Type == "trace_errors").ToArray();
        foreach (var edge in dependencies.Outbound.Where(edge => edge.Source.Key == service.Key))
        {
            var traceSupport = traceEvents
                .Where(item => item.Summary.Contains(
                    edge.Target.ServiceName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => item.EvidenceIds);
            var support = edge.EvidenceIds.Concat(traceSupport)
                .Where(evidenceIds.Contains).Distinct().ToArray();
            var hasTraceMatch = traceSupport.Any();
            var confidence = hasTraceMatch ? 0.72 :
                (edge.ErrorRate ?? 0) >= 0.05 ? 0.62 :
                (edge.LatencyMilliseconds ?? 0) >= 500 ? 0.58 : 0.38;
            if (support.Length == 0 || confidence < 0.50)
                continue;
            candidates.Add(new Candidate(
                $"dependency:{edge.Target.Key}",
                $"Downstream dependency {edge.Target.Key} is a plausible fault origin for {service.Key}.",
                confidence,
                support,
                [],
                dependencies.PotentialBlastRadius
                    .Append(service.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        var anomalyEvidence = timeline.Events
            .Where(item => item.Type.EndsWith("_anomaly", StringComparison.Ordinal) &&
                           item.Severity != FindingSeverity.Info)
            .SelectMany(item => item.EvidenceIds)
            .Where(evidenceIds.Contains)
            .Distinct()
            .ToArray();
        var errorEvidence = timeline.Events
            .Where(item => item.Type is "trace_errors" or "log_error_pattern")
            .SelectMany(item => item.EvidenceIds)
            .Where(evidenceIds.Contains)
            .Distinct()
            .ToArray();
        var serviceSupport = anomalyEvidence.Concat(errorEvidence).Distinct().ToArray();
        if (serviceSupport.Length >= 2 && candidates.All(item =>
                item.Id is not "target_memory_exhaustion" and not "target_crash_loop" and not "recent_deployment"))
        {
            candidates.Add(new Candidate(
                "target_service_regression",
                $"A runtime regression inside {service.Key} is supported by overlapping metric and request evidence.",
                0.60,
                serviceSupport,
                healthyEvidence,
                dependencies.PotentialBlastRadius));
        }

        return candidates
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.BaseConfidence).First())
            .ToList();

        void AddWorkloadCandidate(
            string eventType,
            string id,
            string summary,
            double confidence)
        {
            var support = timeline.Events.Where(item => item.Type == eventType)
                .SelectMany(item => item.EvidenceIds)
                .Where(evidenceIds.Contains)
                .Distinct()
                .ToArray();
            if (support.Length == 0)
                return;
            candidates.Add(new Candidate(
                id, summary, confidence, support, healthyEvidence,
                dependencies.PotentialBlastRadius));
        }
    }

    private static RootCauseHypothesis ToHypothesis(Candidate candidate, double coverage)
    {
        var confidence = candidate.BaseConfidence * (0.60 + 0.40 * coverage);
        confidence += Math.Min(0.08, Math.Max(0, candidate.SupportingEvidenceIds.Count - 1) * 0.02);
        confidence -= Math.Min(0.20, candidate.ContradictingEvidenceIds.Count * 0.04);
        return new RootCauseHypothesis(
            candidate.Id,
            candidate.Summary,
            Math.Round(Math.Clamp(confidence, 0, 0.99), 2),
            candidate.SupportingEvidenceIds,
            candidate.ContradictingEvidenceIds,
            candidate.PotentiallyAffectedServices);
    }

    private static double CalculateCoverage(IReadOnlyList<SourceStatus> sources)
    {
        var total = ExpectedSources.Sum(expected =>
        {
            var matching = sources.Where(source => source.Name.Equals(
                expected, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Any(source => source.Availability == SourceAvailability.Available)) return 1d;
            if (matching.Any(source => source.Availability == SourceAvailability.Stale)) return 0.5d;
            return 0d;
        });
        return Math.Round(total / ExpectedSources.Length, 4);
    }

    private static List<string> BuildLimitations(
        IReadOnlyList<SourceStatus> sources,
        IncidentTimeline timeline,
        DependencyGraph dependencies)
    {
        var limitations = ExpectedSources.Select(expected =>
            {
                var matching = sources.Where(source => source.Name.Equals(
                    expected, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matching.Any(source => source.Availability == SourceAvailability.Available))
                    return null;
                if (matching.Any(source => source.Availability == SourceAvailability.Stale))
                    return $"Source '{expected}' was stale; temporal conclusions have reduced confidence.";
                return $"Source '{expected}' was unavailable; related hypotheses could not be tested.";
            })
            .Where(item => item is not null)
            .Cast<string>()
            .ToList();
        if (timeline.AnalysisConclusion == AnalysisConclusion.Inconclusive)
            limitations.Add("The incident timeline itself was inconclusive.");
        if (dependencies.Nodes.Count <= 1)
            limitations.Add("No resolved dependency path was available for causal propagation analysis.");
        return limitations.Distinct(StringComparer.Ordinal).ToList();
    }

    private static AnalysisResult<RootCauseReport> Result(
        RootCauseReport report,
        IReadOnlyList<SourceStatus> sources,
        IReadOnlyList<Evidence> evidence,
        AnalysisResult<IncidentTimeline> timeline,
        AnalysisResult<HealthReport> health,
        AnalysisResult<DependencyGraph> dependencies) =>
        new(report, sources, evidence,
            timeline.Warnings.Concat(health.Warnings).Concat(dependencies.Warnings)
                .Distinct().ToArray());

    private sealed record Candidate(
        string Id,
        string Summary,
        double BaseConfidence,
        IReadOnlyList<string> SupportingEvidenceIds,
        IReadOnlyList<string> ContradictingEvidenceIds,
        IReadOnlyList<string> PotentiallyAffectedServices);
}
