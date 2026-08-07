using System.Text.Json;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Stores indexing overrides as JSON under the "indexing-overrides" key of a
/// ConfigMap in the server's namespace (default "mcpserver-state"). The ConfigMap
/// is pre-created by the manifests; RBAC grants update/patch only on that object.
/// When it is missing or writes are denied, the store degrades to memory-only
/// with a warning so the toggle keeps working within the pod's lifetime.
/// </summary>
public class KubernetesIndexingStateStore : IIndexingStateStore
{
    private const string DataKey = "indexing-overrides";

    private readonly IKubernetesCollector _k8s;
    private readonly ILogger<KubernetesIndexingStateStore> _logger;
    private readonly string _configMapName;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<string, bool> _memoryFallback = new(StringComparer.OrdinalIgnoreCase);

    public KubernetesIndexingStateStore(
        IKubernetesCollector k8s,
        IConfiguration config,
        ILogger<KubernetesIndexingStateStore> logger)
    {
        _k8s = k8s;
        _logger = logger;
        _configMapName = config["Discovery:StateConfigMap"] ?? "mcpserver-state";
    }

    public async Task<Dictionary<string, bool>> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var data = await _k8s.GetConfigMapDataAsync(_configMapName, ct);
            if (data is null)
            {
                _logger.LogWarning(
                    "State ConfigMap '{Name}' not found; indexing overrides will not survive restarts.",
                    _configMapName);
                return new Dictionary<string, bool>(_memoryFallback, StringComparer.OrdinalIgnoreCase);
            }

            var overrides = Parse(data.GetValueOrDefault(DataKey));

            // Memory fallback fills gaps from writes that failed to persist.
            foreach (var (name, enabled) in _memoryFallback)
                overrides.TryAdd(name, enabled);

            return overrides;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read state ConfigMap '{Name}'; using in-memory overrides only.",
                _configMapName);
            return new Dictionary<string, bool>(_memoryFallback, StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<bool> SaveAsync(string appName, bool enabled, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            _memoryFallback[appName] = enabled;

            var data = await _k8s.GetConfigMapDataAsync(_configMapName, ct);
            if (data is null)
            {
                _logger.LogWarning(
                    "State ConfigMap '{Name}' not found; toggle for '{App}' kept in memory only.",
                    _configMapName, appName);
                return false;
            }

            var overrides = Parse(data.GetValueOrDefault(DataKey));
            overrides[appName] = enabled;
            data[DataKey] = JsonSerializer.Serialize(overrides);

            await _k8s.ReplaceConfigMapDataAsync(_configMapName, data, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist toggle for '{App}' to ConfigMap '{Name}'; kept in memory only.",
                appName, _configMapName);
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static Dictionary<string, bool> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            return parsed is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
