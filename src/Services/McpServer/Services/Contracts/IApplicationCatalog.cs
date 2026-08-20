namespace McpApis.McpServer.Services.Contracts;

using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Domain.Contracts;

/// <summary>
/// Signals that contributed to discovering an application in the cluster.
/// </summary>
[Flags]
public enum DiscoverySources
{
    None = 0,
    /// <summary>Declared in the "Services" configuration section.</summary>
    Config = 1,
    /// <summary>Backed by a Kubernetes Deployment.</summary>
    Deployment = 2,
    /// <summary>Backed by a Kubernetes Service (network reachability).</summary>
    Network = 4,
    /// <summary>Emits traces to the OTel collector (listed by Jaeger /api/services).</summary>
    OpenTelemetry = 8
}

/// <summary>
/// Outcome of the OpenAPI validation (feature 003) for a discovered application.
/// Only affects spec-based tools; an app without a valid spec can still be enabled
/// for trace/health tools.
/// </summary>
public record OpenApiInfo(bool Validated, string? Path, IReadOnlyList<string> Failures)
{
    public static readonly OpenApiInfo NotValidated = new(false, null, []);
}

/// <summary>
/// An application auto-discovered in the cluster by correlating Deployments,
/// Services/Endpoints and OTel trace emitters.
/// </summary>
public record DiscoveredApplication
{
    /// <summary>Canonical normalized name, e.g. "catalogapi".</summary>
    public required string Name { get; init; }
    public string? Namespace { get; init; }
    public DiscoverySources Sources { get; init; }
    public string? DeploymentName { get; init; }
    public string? KubernetesServiceName { get; init; }
    /// <summary>Raw service name as reported by Jaeger (case-sensitive), e.g. "CatalogAPI".</summary>
    public string? OtelServiceName { get; init; }
    /// <summary>Resolved base URL; null for OTel-only applications.</summary>
    public string? BaseUrl { get; init; }
    public IReadOnlyDictionary<string, string> Selector { get; init; } = new Dictionary<string, string>();
    public string? Image { get; init; }
    public string? ImageDigest { get; init; }
    public string? Version { get; init; }
    public string? Revision { get; init; }
    public int DesiredReplicas { get; init; }
    public int ReadyReplicas { get; init; }
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Annotations { get; init; } = new Dictionary<string, string>();
    public string? Owner { get; init; }
    public string? Team { get; init; }
    public string? Description { get; init; }
    public string? MetricsId { get; init; }
    public IReadOnlyList<string> DeclaredDependencies { get; init; } = [];
    public SignalCoverage Coverage { get; init; } = new(
        SourceAvailability.Unavailable,
        SourceAvailability.Unavailable,
        SourceAvailability.Unavailable,
        SourceAvailability.Unavailable,
        SourceAvailability.Unavailable,
        SourceAvailability.Unavailable);
    public bool HasReadyEndpoints { get; init; }
    public required OpenApiInfo OpenApi { get; init; }
    /// <summary>User toggle: whether MCP tools may fetch any data about this app.</summary>
    public bool Enabled { get; init; }
    /// <summary>Label mcp-apis/indexed=false on the Service forces a hard-off (toggle locked).</summary>
    public bool LockedDisabled { get; init; }
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; init; }
}

public enum CatalogResolutionStatus
{
    Resolved,
    Unknown,
    Ambiguous
}

public sealed record CatalogResolution(
    CatalogResolutionStatus Status,
    DiscoveredApplication? Application,
    IReadOnlyList<DiscoveredApplication> Candidates);

/// <summary>
/// Thread-safe live inventory of applications discovered in the cluster.
/// Replaced atomically by each discovery scan; mutated by the dashboard toggle.
/// </summary>
public interface IApplicationCatalog
{
    IReadOnlyList<DiscoveredApplication> GetAll();

    /// <summary>Resolves by canonical name, deployment/service name or OTel service name (case-insensitive).</summary>
    bool TryGet(string nameOrAlias, out DiscoveredApplication app);

    bool TryGet(string nameOrAlias, string namespaceName, out DiscoveredApplication app);

    CatalogResolution Resolve(string nameOrAlias, string? namespaceName = null);

    string? ResolveCanonicalName(string nameOrAlias);

    /// <summary>
    /// True when the name resolves to an enabled app. Unknown names return true
    /// (fail-open) so excluded infrastructure (jaeger, prometheus...) stays reachable.
    /// </summary>
    bool IsEnabled(string nameOrAlias);

    /// <summary>
    /// Installs the result of a discovery scan. Preserves FirstSeen and the user's
    /// Enabled choice for apps already present; keeps apps missing from the scan
    /// until they have not been seen for <paramref name="forgetAfter"/>.
    /// </summary>
    void ReplaceSnapshot(IReadOnlyList<DiscoveredApplication> apps, TimeSpan forgetAfter);

    /// <summary>Returns false when the app is unknown or locked by the mcp-apis/indexed=false label.</summary>
    bool SetEnabled(string nameOrAlias, bool enabled, string? namespaceName = null);
}
