using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace McpApis.McpServer.Infrastructure.Options;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClusterAccessScope
{
    Cluster,
    Namespace,
    None
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClusterStateStorage
{
    ConfigMap,
    Memory
}

/// <summary>
/// Declares the Kubernetes capabilities that the installation is allowed to
/// use. The Helm chart renders RBAC, state storage and writable volumes from
/// the same values so the runtime contract cannot silently exceed the
/// installation contract.
/// </summary>
public sealed class ClusterAccessOptions
{
    public const string SectionName = "ClusterAccess";

    public ClusterAccessScope Scope { get; init; } = ClusterAccessScope.Cluster;
    public bool ServiceDiscovery { get; init; } = true;
    public ClusterStateStorage StateStorage { get; init; } = ClusterStateStorage.ConfigMap;
    public bool AllowVolumes { get; init; } = true;
    public bool ValidateOnStart { get; init; } = true;

    [Range(5, 600)]
    public int ValidationCacheSeconds { get; init; } = 30;

    public string EffectiveMode =>
        Scope == ClusterAccessScope.None && !ServiceDiscovery && !AllowVolumes
            ? "restricted"
            : !ServiceDiscovery
                ? "no-service-discovery"
                : !AllowVolumes
                    ? "no-volumes"
                    : Scope == ClusterAccessScope.Namespace
                        ? "namespace-only"
                        : "cluster";
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public AuthenticationOptions Authentication { get; init; } = new();

    [MinLength(1)]
    public string[] AllowedNamespaces { get; init; } = [];

    public string[] AllowedServiceHostSuffixes { get; init; } = [".svc.cluster.local"];
    public int[] AllowedServicePorts { get; init; } = [80, 443, 8080];
}

public sealed class AuthenticationOptions
{
    public bool Enabled { get; init; } = true;
    public string HeaderName { get; init; } = "X-Observability-Api-Key";
    public string? ReaderApiKey { get; init; }
    public string? AdminApiKey { get; init; }
}

public sealed class ObservabilityLimitsOptions
{
    public const string SectionName = "Observability:Limits";

    [Range(1, 1440)]
    public int DefaultWindowMinutes { get; init; } = 30;

    [Range(1, 10080)]
    public int MaxWindowMinutes { get; init; } = 1440;

    [Range(1, 300)]
    public int ProviderTimeoutSeconds { get; init; } = 10;

    [Range(1, 600)]
    public int ToolTimeoutSeconds { get; init; } = 30;

    [Range(1, 1000)]
    public int MaxTraces { get; init; } = 50;

    [Range(1, 10000)]
    public int MaxSpans { get; init; } = 1000;

    [Range(1, 5000)]
    public int MaxLogs { get; init; } = 200;

    [Range(1, 1000)]
    public int MaxDependencies { get; init; } = 100;

    [Range(1, 10)]
    public int MaxGraphDepth { get; init; } = 4;

    [Range(1, 3600)]
    public int MinimumRangeStepSeconds { get; init; } = 15;

    [Range(1024, 10_485_760)]
    public int MaxResponseBytes { get; init; } = 524_288;

    [Range(1024, 1_048_576)]
    public int MaxCapturedBodyBytes { get; init; } = 16_384;

    [Range(1, 1000)]
    public int RateLimitRequestsPerMinute { get; init; } = 120;

    [Range(1, 100)]
    public int ConcurrencyLimit { get; init; } = 16;
}

public sealed class HealthEngineOptions
{
    public const string SectionName = "Observability:Health";

    public Dictionary<string, double> Weights { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["availability"] = 30,
        ["errors"] = 25,
        ["latency"] = 20,
        ["saturation"] = 15,
        ["stability"] = 10
    };

    public string[] RequiredDimensions { get; init; } =
        ["availability", "errors", "latency", "stability"];

    [Range(0, 1)]
    public double MinimumCoverage { get; init; } = 0.60;

    [Range(0, 1)]
    public double HealthyCoverage { get; init; } = 0.80;

    [Range(0, 100)]
    public double HealthyScore { get; init; } = 85;

    [Range(0, 100)]
    public double DegradedScore { get; init; } = 60;

    [Range(0, 1)]
    public double WarningErrorRate { get; init; } = 0.02;

    [Range(0, 1)]
    public double CriticalErrorRate { get; init; } = 0.10;

    [Range(1, 300_000)]
    public double WarningP95Milliseconds { get; init; } = 500;

    [Range(1, 300_000)]
    public double CriticalP95Milliseconds { get; init; } = 2000;

    [Range(1, 100)]
    public int WarningRestarts { get; init; } = 1;

    [Range(1, 1000)]
    public int CriticalRestarts { get; init; } = 5;
}

public sealed class MetricsTemplateOptions
{
    public const string SectionName = "Observability:Metrics";

    public string ServiceLabel { get; init; } = "job";
    public string RequestDurationCountMetric { get; init; } = "http_server_request_duration_seconds_count";
    public string RequestDurationBucketMetric { get; init; } = "http_server_request_duration_seconds_bucket";
    public string StatusCodeLabel { get; init; } = "http_response_status_code";
    public string AvailabilityMetric { get; init; } = "up";
    public string CpuMetric { get; init; } = "dotnet_process_cpu_time_seconds_total";
    public string MemoryMetric { get; init; } = "dotnet_process_memory_working_set_bytes";
}

public sealed class ObservabilityCacheOptions
{
    public const string SectionName = "Observability:Cache";

    [Range(1, 3600)]
    public int HealthTtlSeconds { get; init; } = 20;

    [Range(1, 3600)]
    public int DependencyTtlSeconds { get; init; } = 120;

    [Range(1, 86400)]
    public int SpecTtlSeconds { get; init; } = 900;

    [Range(1, 3600)]
    public int KubernetesTtlSeconds { get; init; } = 20;
}

public sealed class ObservabilityFeatureOptions
{
    public const string SectionName = "Observability:Features";

    public bool EnableRawQueries { get; init; }
    public bool EnableLogs { get; init; } = true;
    public bool EnableDeploymentEvents { get; init; } = true;
}
