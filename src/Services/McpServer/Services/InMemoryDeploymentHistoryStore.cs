using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Bounded, pod-local deployment history for installations without ConfigMap
/// persistence. It deliberately reports only changes observed during the
/// current process lifetime.
/// </summary>
public sealed class InMemoryDeploymentHistoryStore : IDeploymentHistoryStore
{
    private const int MaxChanges = 500;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Snapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StoredChange> _changes = [];

    public async Task ObserveAsync(
        IReadOnlyList<DiscoveredApplication> applications,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var application in applications.Where(app =>
                         app.Namespace is not null && app.DeploymentName is not null))
            {
                var key = Key(application.Namespace!, application.Name);
                var current = new Snapshot(
                    application.Revision,
                    application.Image,
                    application.DesiredReplicas,
                    application.ReadyReplicas);
                if (_snapshots.TryGetValue(key, out var previous))
                {
                    if (!string.Equals(previous.Revision, current.Revision, StringComparison.Ordinal) ||
                        !string.Equals(previous.Image, current.Image, StringComparison.Ordinal))
                    {
                        Add(key, new DeploymentChange(
                            Id(key, "version", observedAt), observedAt, "version_change",
                            $"Deployment changed from revision '{previous.Revision ?? "unknown"}' / image '{previous.Image ?? "unknown"}' to revision '{current.Revision ?? "unknown"}' / image '{current.Image ?? "unknown"}'.",
                            current.Revision, current.Image, []));
                    }
                    if (previous.DesiredReplicas != current.DesiredReplicas)
                    {
                        Add(key, new DeploymentChange(
                            Id(key, "scale", observedAt), observedAt, "scale_change",
                            $"Desired replicas changed from {previous.DesiredReplicas} to {current.DesiredReplicas}.",
                            current.Revision, current.Image, []));
                    }
                }
                _snapshots[key] = current;
            }

            _changes.RemoveAll(change => change.Change.Timestamp < observedAt - Retention);
            if (_changes.Count > MaxChanges)
                _changes.RemoveRange(0, _changes.Count - MaxChanges);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DeploymentChange>> GetChangesAsync(
        ServiceIdentity service,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var key = Key(service.Namespace, service.ServiceName);
            return _changes
                .Where(change => change.ServiceKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                .Select(change => change.Change)
                .Where(change => change.Timestamp >= window.From && change.Timestamp <= window.To)
                .OrderBy(change => change.Timestamp)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Add(string key, DeploymentChange change)
    {
        if (_changes.All(existing => existing.Change.Id != change.Id))
            _changes.Add(new StoredChange(key, change));
    }

    private static string Key(string namespaceName, string serviceName) =>
        $"{namespaceName}/{serviceName}".ToLowerInvariant();

    private static string Id(string key, string type, DateTimeOffset timestamp) =>
        $"history:{key}:{type}:{timestamp.ToUnixTimeMilliseconds()}";

    private sealed record Snapshot(
        string? Revision,
        string? Image,
        int DesiredReplicas,
        int ReadyReplicas);
    private sealed record StoredChange(string ServiceKey, DeploymentChange Change);
}
