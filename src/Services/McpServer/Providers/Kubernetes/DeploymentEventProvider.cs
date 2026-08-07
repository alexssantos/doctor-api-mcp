using System.Diagnostics;
using Microsoft.Extensions.Options;
using McpApis.BuildingBlocks.Observability;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Providers.Kubernetes;

public sealed class DeploymentEventProvider(
    IKubernetesProvider kubernetes,
    IDeploymentHistoryStore history,
    IOptions<ObservabilityFeatureOptions> features) : IDeploymentEventProvider
{
    private static readonly IReadOnlySet<string> SensitiveFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "password", "secret", "token", "authorization", "cookie", "apiKey" };

    public async Task<ProviderResult<IReadOnlyList<DeploymentChange>>> GetChangesAsync(
        ServiceIdentity service,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        if (!features.Value.EnableDeploymentEvents)
            return ProviderResult<IReadOnlyList<DeploymentChange>>.Unavailable(
                "deployments", 0, "Deployment event integration is disabled by feature policy.");

        var historyTask = history.GetChangesAsync(service, window, cancellationToken);
        var eventsTask = kubernetes.GetEventsAsync(service, window, cancellationToken);
        await Task.WhenAll(historyTask, eventsTask);
        var persisted = await historyTask;
        var events = await eventsTask;
        var eventChanges = (events.Value ?? [])
            .Where(IsDeploymentEvent)
            .Select(item => new DeploymentChange(
                $"event:{item.Id}",
                item.Timestamp,
                EventType(item),
                $"{item.Reason}: {SensitiveDataRedactor.Redact(item.Message, SensitiveFields)}",
                null,
                null,
                []));
        var changes = persisted.Concat(eventChanges)
            .GroupBy(change => change.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(change => change.Timestamp)
            .ToArray();
        var elapsed = Stopwatch.GetElapsedTime(started);

        if (events.Availability == SourceAvailability.Unavailable)
        {
            if (changes.Length == 0)
                return ProviderResult<IReadOnlyList<DeploymentChange>>.Unavailable(
                    "deployments", (long)elapsed.TotalMilliseconds, events.Warnings.ToArray());
            return ProviderResult<IReadOnlyList<DeploymentChange>>.Stale(
                "deployments", changes, changes.Max(change => change.Timestamp),
                (long)elapsed.TotalMilliseconds,
                events.Warnings.Concat(["Only persisted deployment history was available."]).ToArray());
        }

        var observedAt = changes.Length == 0
            ? events.ObservedAt ?? DateTimeOffset.UtcNow
            : changes.Max(change => change.Timestamp);
        return ProviderResult<IReadOnlyList<DeploymentChange>>.Available(
            "deployments", changes, observedAt, (long)elapsed.TotalMilliseconds,
            events.Warnings.ToArray());
    }

    private static bool IsDeploymentEvent(KubernetesEventRecord item) =>
        item.InvolvedKind.Equals("Deployment", StringComparison.OrdinalIgnoreCase) ||
        item.InvolvedKind.Equals("ReplicaSet", StringComparison.OrdinalIgnoreCase) ||
        item.Reason.Contains("ScalingReplicaSet", StringComparison.OrdinalIgnoreCase) ||
        item.Reason.Contains("SuccessfulCreate", StringComparison.OrdinalIgnoreCase) ||
        item.Message.Contains("scaled", StringComparison.OrdinalIgnoreCase) ||
        item.Message.Contains("rollout", StringComparison.OrdinalIgnoreCase);

    private static string EventType(KubernetesEventRecord item) =>
        item.Reason.Contains("scal", StringComparison.OrdinalIgnoreCase) ||
        item.Message.Contains("scaled", StringComparison.OrdinalIgnoreCase)
            ? "scale_event"
            : "rollout_event";
}
