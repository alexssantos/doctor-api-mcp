using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Thread-safe implementation of the application inventory. All reads work on an
/// immutable snapshot swapped atomically; writes (scan install / toggle) are
/// serialized under a single lock so a toggle can never be lost to a concurrent scan.
/// </summary>
public class ApplicationCatalog : IApplicationCatalog
{
    private readonly object _gate = new();
    private volatile IReadOnlyList<DiscoveredApplication> _snapshot = [];
    private volatile Dictionary<string, string> _aliasIndex = new(StringComparer.Ordinal);

    public IReadOnlyList<DiscoveredApplication> GetAll() => _snapshot;

    public bool TryGet(string nameOrAlias, out DiscoveredApplication app)
    {
        var canonical = ResolveCanonicalName(nameOrAlias);
        if (canonical is not null)
        {
            var match = _snapshot.FirstOrDefault(a =>
                a.Name.Equals(canonical, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                app = match;
                return true;
            }
        }

        app = null!;
        return false;
    }

    public string? ResolveCanonicalName(string nameOrAlias)
    {
        var key = NameNormalizer.Normalize(nameOrAlias);
        return key.Length > 0 && _aliasIndex.TryGetValue(key, out var canonical)
            ? canonical
            : null;
    }

    public bool IsEnabled(string nameOrAlias) =>
        !TryGet(nameOrAlias, out var app) || app.Enabled;

    public void ReplaceSnapshot(IReadOnlyList<DiscoveredApplication> apps, TimeSpan forgetAfter)
    {
        lock (_gate)
        {
            var previous = _snapshot.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
            var merged = new List<DiscoveredApplication>(apps.Count);

            foreach (var incoming in apps)
            {
                if (previous.Remove(incoming.Name, out var existing))
                {
                    // Preserve FirstSeen and the user's latest toggle; the label
                    // hard-off from the fresh scan always wins.
                    merged.Add(incoming with
                    {
                        FirstSeen = existing.FirstSeen,
                        Enabled = !incoming.LockedDisabled && existing.Enabled
                    });
                }
                else
                {
                    merged.Add(incoming);
                }
            }

            // Apps not seen by this scan linger (flagged "missing" by consumers via
            // LastSeen) until the forget window elapses.
            var cutoff = DateTimeOffset.UtcNow - forgetAfter;
            merged.AddRange(previous.Values.Where(a => a.LastSeen >= cutoff));

            Install(merged);
        }
    }

    public bool SetEnabled(string nameOrAlias, bool enabled)
    {
        lock (_gate)
        {
            var canonical = ResolveCanonicalName(nameOrAlias);
            if (canonical is null)
                return false;

            var updated = new List<DiscoveredApplication>(_snapshot.Count);
            var changed = false;
            foreach (var app in _snapshot)
            {
                if (app.Name.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                {
                    if (app.LockedDisabled)
                        return false;
                    updated.Add(app with { Enabled = enabled });
                    changed = true;
                }
                else
                {
                    updated.Add(app);
                }
            }

            if (changed)
                Install(updated);
            return changed;
        }
    }

    /// <summary>Swaps the snapshot and rebuilds the alias → canonical name index. Callers hold the lock.</summary>
    private void Install(List<DiscoveredApplication> apps)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            foreach (var alias in new[] { app.Name, app.DeploymentName, app.KubernetesServiceName, app.OtelServiceName })
            {
                var key = NameNormalizer.Normalize(alias);
                if (key.Length > 0)
                    index.TryAdd(key, app.Name);
            }
        }

        _aliasIndex = index;
        _snapshot = apps;
    }
}
