using McpApis.McpServer.Domain.Contracts;

namespace McpApis.McpServer.Domain.Models;

public sealed record SignalCoverage(
    SourceAvailability Kubernetes,
    SourceAvailability Metrics,
    SourceAvailability Traces,
    SourceAvailability Logs,
    SourceAvailability OpenApi,
    SourceAvailability Events);

public sealed record ApiEndpointSummary(
    string Method,
    string Path,
    string? Summary,
    string? OperationId,
    IReadOnlyList<string> ResponseCodes);

public sealed record ServiceSpecReport(
    string? Description,
    string? Owner,
    string? Team,
    string? Version,
    string? Image,
    string? ImageDigest,
    string? Revision,
    int DesiredReplicas,
    int ReadyReplicas,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations,
    IReadOnlyDictionary<string, string> Selector,
    SignalCoverage Coverage,
    IReadOnlyList<ApiEndpointSummary> Endpoints,
    IReadOnlyList<string> DeclaredDependencies);

public sealed record KubernetesPodState(
    string Name,
    string Phase,
    bool Ready,
    int Restarts,
    bool OomKilled,
    bool CrashLoopBackOff,
    bool Pending,
    IReadOnlyList<string> ContainerStates,
    IReadOnlyDictionary<string, string> ResourceRequests,
    IReadOnlyDictionary<string, string> ResourceLimits);

public sealed record KubernetesWorkloadState(
    string DeploymentName,
    int DesiredReplicas,
    int ReadyReplicas,
    int AvailableReplicas,
    string? Revision,
    string? Image,
    string? ImageDigest,
    IReadOnlyDictionary<string, string> Selector,
    IReadOnlyList<KubernetesPodState> Pods,
    int RestartCount,
    bool AllReady,
    bool HasPods);

public sealed record RedMetrics(
    Measurement? RequestRate,
    Measurement? ErrorRate,
    Measurement? P50Latency,
    Measurement? P95Latency,
    Measurement? P99Latency,
    Measurement? Availability,
    Measurement? CpuUsage,
    Measurement? MemoryUsage);

public sealed record MetricPoint(DateTimeOffset Timestamp, double Value);

public sealed record MetricSeries(
    string Metric,
    string Unit,
    string Aggregation,
    IReadOnlyList<MetricPoint> Points);

public enum MetricSignal
{
    RequestRate,
    ErrorRate,
    P95Latency,
    Availability,
    CpuUsage,
    MemoryUsage
}

public sealed record HealthDimension(
    string Name,
    double Weight,
    double? Score,
    bool Required,
    SourceAvailability Availability,
    IReadOnlyList<string> EvidenceIds);

public sealed record HealthReport(
    HealthState HealthStatus,
    double? Score,
    double Coverage,
    IReadOnlyList<HealthDimension> Dimensions,
    IReadOnlyList<Finding> Findings,
    DateTimeOffset EvaluatedAt);

public sealed record HealthScoreProjection(
    HealthState HealthStatus,
    double? Score,
    double Coverage,
    DateTimeOffset EvaluatedAt);

public sealed record NormalizedSpan(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string ServiceName,
    string OperationName,
    DateTimeOffset StartedAt,
    double DurationMilliseconds,
    string SpanStatus,
    bool HasError,
    string? PeerService,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<string> Events,
    bool Redacted);

public sealed record DependencyObservation(
    string SourceService,
    string TargetService,
    DateTimeOffset ObservedAt,
    long CallCount,
    long ErrorCount,
    double? AverageLatencyMilliseconds,
    string Source,
    IReadOnlyList<string> TraceIds);

public sealed record DependencyEdge(
    ServiceIdentity Source,
    ServiceIdentity Target,
    string Type,
    DateTimeOffset ObservedAt,
    long CallCount,
    double? ErrorRate,
    double? LatencyMilliseconds,
    IReadOnlyList<string> EvidenceIds,
    bool Declared,
    bool Observed);

public sealed record DependencyGraph(
    ServiceIdentity Root,
    int Depth,
    IReadOnlyList<ServiceIdentity> Nodes,
    IReadOnlyList<DependencyEdge> Inbound,
    IReadOnlyList<DependencyEdge> Outbound,
    IReadOnlyList<IReadOnlyList<string>> Cycles,
    IReadOnlyList<string> CriticalPath,
    IReadOnlyList<string> PotentialBlastRadius);

public sealed record Anomaly(
    string Metric,
    AnalysisConclusion Conclusion,
    FindingSeverity Severity,
    double? CurrentValue,
    double? ExpectedValue,
    double? Deviation,
    string Unit,
    string Method,
    int SampleCount,
    DateTimeOffset? EstimatedStart,
    IReadOnlyList<string> EvidenceIds);

public sealed record AnomalyReport(
    AnalysisConclusion AnalysisConclusion,
    IReadOnlyList<Anomaly> Anomalies,
    DateTimeOffset EvaluatedAt);

public sealed record LogPattern(
    string Fingerprint,
    string Level,
    string Message,
    int Count,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    string? TraceId,
    string? Pod,
    bool Redacted);

public sealed record KubernetesEventRecord(
    string Id,
    DateTimeOffset Timestamp,
    string Type,
    string Reason,
    string Message,
    string InvolvedKind,
    string InvolvedName,
    int Count);

public sealed record DeploymentChange(
    string Id,
    DateTimeOffset Timestamp,
    string Type,
    string Summary,
    string? Revision,
    string? Image,
    IReadOnlyList<string> EvidenceIds);

public sealed record IncidentEvent(
    string Id,
    DateTimeOffset Timestamp,
    string Type,
    ServiceIdentity Service,
    FindingSeverity Severity,
    string Source,
    string Summary,
    IReadOnlyList<string> EvidenceIds);

public sealed record IncidentTimeline(
    AnalysisConclusion AnalysisConclusion,
    DateTimeOffset? IncidentStartedAt,
    IReadOnlyList<IncidentEvent> Events,
    IReadOnlyList<string> Correlations);

public sealed record RootCauseHypothesis(
    string Id,
    string Summary,
    double Confidence,
    IReadOnlyList<string> SupportingEvidenceIds,
    IReadOnlyList<string> ContradictingEvidenceIds,
    IReadOnlyList<string> PotentiallyAffectedServices);

public sealed record Recommendation(
    string Priority,
    string Action,
    string Reason,
    IReadOnlyList<string> EvidenceIds,
    bool Executable = false);

public sealed record RootCauseReport(
    AnalysisConclusion AnalysisConclusion,
    RootCauseHypothesis? PrimaryHypothesis,
    IReadOnlyList<RootCauseHypothesis> Alternatives,
    double Coverage,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<Recommendation> Recommendations);

public sealed record ServiceHealthSummary(
    ServiceIdentity Service,
    HealthState HealthStatus,
    double? Score,
    double Coverage,
    int CriticalFindings,
    DateTimeOffset EvaluatedAt);

public sealed record SystemHealthSummary(
    HealthState HealthStatus,
    int TotalServices,
    int Healthy,
    int Degraded,
    int Critical,
    int Unknown,
    IReadOnlyList<ServiceHealthSummary> Services,
    DateTimeOffset EvaluatedAt);
