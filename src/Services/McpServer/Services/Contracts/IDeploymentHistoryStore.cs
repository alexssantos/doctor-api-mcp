using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;

namespace McpApis.McpServer.Services.Contracts;

/// <summary>
/// Keeps a bounded history of catalog-observed deployment/image/replica changes.
/// The implementation persists into the MCP's own pre-created state ConfigMap,
/// so useful rollout evidence can outlive Kubernetes Event retention and pod restarts.
/// </summary>
public interface IDeploymentHistoryStore
{
    Task ObserveAsync(
        IReadOnlyList<DiscoveredApplication> applications,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeploymentChange>> GetChangesAsync(
        ServiceIdentity service,
        TimeWindow window,
        CancellationToken cancellationToken = default);
}
