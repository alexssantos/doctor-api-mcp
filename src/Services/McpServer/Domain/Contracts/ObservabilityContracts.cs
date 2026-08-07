using System.Text.Json.Serialization;

namespace McpApis.McpServer.Domain.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutionStatus
{
    [JsonStringEnumMemberName("complete")]
    Complete,
    [JsonStringEnumMemberName("partial")]
    Partial,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceAvailability
{
    [JsonStringEnumMemberName("available")]
    Available,
    [JsonStringEnumMemberName("stale")]
    Stale,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HealthState
{
    [JsonStringEnumMemberName("healthy")]
    Healthy,
    [JsonStringEnumMemberName("degraded")]
    Degraded,
    [JsonStringEnumMemberName("critical")]
    Critical,
    [JsonStringEnumMemberName("unknown")]
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnalysisConclusion
{
    [JsonStringEnumMemberName("detected")]
    Detected,
    [JsonStringEnumMemberName("notDetected")]
    NotDetected,
    [JsonStringEnumMemberName("inconclusive")]
    Inconclusive
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FindingSeverity
{
    [JsonStringEnumMemberName("info")]
    Info,
    [JsonStringEnumMemberName("warning")]
    Warning,
    [JsonStringEnumMemberName("critical")]
    Critical
}

public sealed record ServiceIdentity(
    string ServiceName,
    string Namespace,
    string? DeploymentName,
    string? KubernetesServiceName,
    string? OtelServiceName,
    string? MetricsId,
    IReadOnlyList<string> Aliases)
{
    [JsonIgnore]
    public string Key => $"{Namespace}/{ServiceName}";
}

public sealed record TimeWindow(
    DateTimeOffset From,
    DateTimeOffset To,
    string Duration,
    string Timezone = "UTC")
{
    [JsonIgnore]
    public TimeSpan Span => To - From;

    public static TimeWindow EndingAt(DateTimeOffset to, TimeSpan duration) =>
        new(to - duration, to, Format(duration));

    public static string Format(TimeSpan value) =>
        value.TotalDays >= 1 ? $"{value.TotalDays:0.##}d" :
        value.TotalHours >= 1 ? $"{value.TotalHours:0.##}h" :
        value.TotalMinutes >= 1 ? $"{value.TotalMinutes:0.##}m" :
        $"{value.TotalSeconds:0.##}s";
}

public sealed record SourceStatus(
    string Name,
    SourceAvailability Availability,
    DateTimeOffset? ObservedAt,
    long? FreshnessSeconds,
    long ElapsedMilliseconds,
    IReadOnlyList<string> Warnings);

public sealed record Evidence(
    string Id,
    string Source,
    string Signal,
    double? Value,
    double? Baseline,
    string? Unit,
    DateTimeOffset Timestamp,
    string QueryDescriptor,
    string? Detail = null);

public sealed record Finding(
    string Type,
    FindingSeverity Severity,
    ServiceIdentity Service,
    string Message,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> EvidenceIds);

public sealed record Measurement(
    string Metric,
    double Value,
    string Unit,
    DateTimeOffset Timestamp,
    string Aggregation);

public sealed record ToolError(
    string Code,
    string Message,
    IReadOnlyList<string>? Candidates = null,
    string? Recovery = null);

public sealed record ProviderResult<T>(
    string Source,
    SourceAvailability Availability,
    T? Value,
    DateTimeOffset? ObservedAt,
    long? FreshnessSeconds,
    IReadOnlyList<string> Warnings,
    long ElapsedMilliseconds)
{
    public SourceStatus ToSourceStatus() =>
        new(Source, Availability, ObservedAt, FreshnessSeconds, ElapsedMilliseconds, Warnings);

    public static ProviderResult<T> Available(
        string source,
        T value,
        DateTimeOffset observedAt,
        long elapsedMilliseconds,
        params string[] warnings) =>
        new(source, SourceAvailability.Available, value, observedAt,
            Math.Max(0, (long)(DateTimeOffset.UtcNow - observedAt).TotalSeconds),
            warnings, elapsedMilliseconds);

    public static ProviderResult<T> Stale(
        string source,
        T value,
        DateTimeOffset observedAt,
        long elapsedMilliseconds,
        params string[] warnings) =>
        new(source, SourceAvailability.Stale, value, observedAt,
            Math.Max(0, (long)(DateTimeOffset.UtcNow - observedAt).TotalSeconds),
            warnings, elapsedMilliseconds);

    public static ProviderResult<T> Unavailable(
        string source,
        long elapsedMilliseconds,
        params string[] warnings) =>
        new(source, SourceAvailability.Unavailable, default, null, null,
            warnings.Length == 0 ? ["Source unavailable."] : warnings,
            elapsedMilliseconds);
}

public sealed record AnalysisResult<T>(
    T Data,
    IReadOnlyList<SourceStatus> Sources,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record ObservationEnvelope<T>
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public required ExecutionStatus ExecutionStatus { get; init; }
    public ServiceIdentity? Service { get; init; }
    public TimeWindow? Window { get; init; }
    public T? Data { get; init; }
    public IReadOnlyList<SourceStatus> Sources { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<Evidence> Evidence { get; init; } = [];
    public ToolError? Error { get; init; }

    public static ObservationEnvelope<T> Success(
        T data,
        ServiceIdentity? service,
        TimeWindow? window,
        IReadOnlyList<SourceStatus> sources,
        IReadOnlyList<Evidence>? evidence = null,
        IReadOnlyList<string>? warnings = null)
    {
        var status = sources.Count == 0 || sources.All(s => s.Availability == SourceAvailability.Available)
            ? ExecutionStatus.Complete
            : sources.Any(s => s.Availability != SourceAvailability.Unavailable)
                ? ExecutionStatus.Partial
                : ExecutionStatus.Unavailable;

        return new ObservationEnvelope<T>
        {
            ExecutionStatus = status,
            Service = service,
            Window = window,
            Data = data,
            Sources = sources,
            Evidence = evidence ?? [],
            Warnings = warnings ?? sources.SelectMany(s => s.Warnings).Distinct().ToArray()
        };
    }

    public static ObservationEnvelope<T> Failure(
        string code,
        string message,
        ServiceIdentity? service = null,
        TimeWindow? window = null,
        IReadOnlyList<string>? candidates = null,
        string? recovery = null) =>
        new()
        {
            ExecutionStatus = ExecutionStatus.Unavailable,
            Service = service,
            Window = window,
            Error = new ToolError(code, message, candidates, recovery),
            Warnings = [message]
        };
}
