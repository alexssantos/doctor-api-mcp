namespace McpApis.McpServer.Services.Contracts;

public record DiscoveryScanResult(
    int Discovered,
    int Validated,
    int OtelOnly,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Runs discovery scans that correlate Deployments, Services/Endpoints and OTel
/// trace emitters into the application catalog. Executed once (blocking) at
/// startup, then periodically or on demand by the discovery background service.
/// </summary>
public interface IDiscoveryOrchestrator
{
    Task<DiscoveryScanResult> ScanAsync(CancellationToken ct = default);

    /// <summary>Signals the background service to run a scan as soon as possible.</summary>
    void RequestRescan();

    DateTimeOffset? LastScanCompletedAt { get; }
}
