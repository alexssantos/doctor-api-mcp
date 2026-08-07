using System.Text.Json;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

public sealed class KubernetesDeploymentHistoryStore : IDeploymentHistoryStore
{
    private const string DataKey = "deployment-history";
    private const int MaxChanges = 500;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly IKubernetesCollector _kubernetes;
    private readonly ILogger<KubernetesDeploymentHistoryStore> _logger;
    private readonly string _configMapName;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DeploymentSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StoredChange> _changes = [];
    private bool _loaded;

    public KubernetesDeploymentHistoryStore(
        IKubernetesCollector kubernetes,
        IConfiguration configuration,
        ILogger<KubernetesDeploymentHistoryStore> logger)
    {
        _kubernetes = kubernetes;
        _logger = logger;
        _configMapName = configuration["Discovery:StateConfigMap"] ?? "mcpserver-state";
    }

    public async Task ObserveAsync(
        IReadOnlyList<DiscoveredApplication> applications,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            var changed = false;
            foreach (var application in applications.Where(app =>
                         app.Namespace is not null && app.DeploymentName is not null))
            {
                var key = Key(application.Namespace!, application.Name);
                var current = new DeploymentSnapshot(
                    key,
                    application.Name,
                    application.Namespace!,
                    application.Revision,
                    application.Image,
                    application.DesiredReplicas,
                    application.ReadyReplicas,
                    observedAt);

                var snapshotChanged = false;
                if (_snapshots.TryGetValue(key, out var previous))
                {
                    if (!string.Equals(previous.Revision, current.Revision, StringComparison.Ordinal) ||
                        !string.Equals(previous.Image, current.Image, StringComparison.Ordinal))
                    {
                        AddChange(key, new DeploymentChange(
                            Id(key, "version", observedAt),
                            observedAt,
                            "version_change",
                            $"Deployment changed from revision '{previous.Revision ?? "unknown"}' / image '{previous.Image ?? "unknown"}' to revision '{current.Revision ?? "unknown"}' / image '{current.Image ?? "unknown"}'.",
                            current.Revision,
                            current.Image,
                            []));
                        changed = true;
                        snapshotChanged = true;
                    }

                    if (previous.DesiredReplicas != current.DesiredReplicas)
                    {
                        AddChange(key, new DeploymentChange(
                            Id(key, "scale", observedAt),
                            observedAt,
                            "scale_change",
                            $"Desired replicas changed from {previous.DesiredReplicas} to {current.DesiredReplicas}.",
                            current.Revision,
                            current.Image,
                            []));
                        changed = true;
                        snapshotChanged = true;
                    }

                    if (previous.ReadyReplicas != current.ReadyReplicas)
                        snapshotChanged = true;
                }
                else
                {
                    changed = true;
                    snapshotChanged = true;
                }

                if (snapshotChanged)
                    _snapshots[key] = current;
            }

            var cutoff = observedAt - Retention;
            changed |= _changes.RemoveAll(change => change.Change.Timestamp < cutoff) > 0;
            if (_changes.Count > MaxChanges)
            {
                _changes.RemoveRange(0, _changes.Count - MaxChanges);
                changed = true;
            }

            if (changed)
                await PersistAsync(cancellationToken);
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
            await EnsureLoadedAsync(cancellationToken);
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

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            var data = await _kubernetes.GetConfigMapDataAsync(_configMapName, cancellationToken);
            if (data is null || !data.TryGetValue(DataKey, out var json) || string.IsNullOrWhiteSpace(json))
                return;
            var state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state is null)
                return;
            foreach (var snapshot in state.Snapshots)
                _snapshots[snapshot.ServiceKey] = snapshot;
            _changes.AddRange(state.Changes);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            _logger.LogWarning(ex,
                "Deployment history could not be loaded from ConfigMap {ConfigMap}; using memory only.",
                _configMapName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Deployment history state is unavailable; using memory only.");
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        try
        {
            var data = await _kubernetes.GetConfigMapDataAsync(_configMapName, cancellationToken);
            if (data is null)
            {
                _logger.LogWarning(
                    "State ConfigMap {ConfigMap} is missing; deployment history remains in memory.",
                    _configMapName);
                return;
            }

            var state = new PersistedState(_snapshots.Values.ToArray(), _changes.ToArray());
            data[DataKey] = JsonSerializer.Serialize(state);
            await _kubernetes.ReplaceConfigMapDataAsync(_configMapName, data, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Deployment history could not be persisted; current history remains in memory.");
        }
    }

    private void AddChange(string key, DeploymentChange change)
    {
        if (_changes.All(existing => existing.Change.Id != change.Id))
            _changes.Add(new StoredChange(key, change));
    }

    private static string Key(string namespaceName, string serviceName) =>
        $"{namespaceName}/{serviceName}".ToLowerInvariant();

    private static string Id(string key, string type, DateTimeOffset timestamp) =>
        $"history:{key}:{type}:{timestamp.ToUnixTimeMilliseconds()}";

    private sealed record DeploymentSnapshot(
        string ServiceKey,
        string ServiceName,
        string Namespace,
        string? Revision,
        string? Image,
        int DesiredReplicas,
        int ReadyReplicas,
        DateTimeOffset ObservedAt);

    private sealed record StoredChange(string ServiceKey, DeploymentChange Change);
    private sealed record PersistedState(
        IReadOnlyList<DeploymentSnapshot> Snapshots,
        IReadOnlyList<StoredChange> Changes);
}
