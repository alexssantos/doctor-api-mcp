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
    private volatile Dictionary<string, string[]> _aliasIndex = new(StringComparer.Ordinal);

    public IReadOnlyList<DiscoveredApplication> GetAll() => _snapshot;

    public bool TryGet(string nameOrAlias, out DiscoveredApplication app)
    {
        var resolution = Resolve(nameOrAlias);
        app = resolution.Application!;
        return resolution.Status == CatalogResolutionStatus.Resolved;
    }

    public bool TryGet(string nameOrAlias, string namespaceName, out DiscoveredApplication app)
    {
        var resolution = Resolve(nameOrAlias, namespaceName);
        app = resolution.Application!;
        return resolution.Status == CatalogResolutionStatus.Resolved;
    }

    public string? ResolveCanonicalName(string nameOrAlias)
    {
        var resolution = Resolve(nameOrAlias);
        return resolution.Status == CatalogResolutionStatus.Resolved
            ? resolution.Application!.Name
            : null;
    }

    public CatalogResolution Resolve(string nameOrAlias, string? namespaceName = null)
    {
        if (string.IsNullOrWhiteSpace(nameOrAlias))
            return new CatalogResolution(CatalogResolutionStatus.Unknown, null, []);

        if (namespaceName is null && nameOrAlias.Contains('/', StringComparison.Ordinal))
        {
            var parts = nameOrAlias.Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                namespaceName = parts[0];
                nameOrAlias = parts[1];
            }
        }

        var alias = NameNormalizer.Normalize(nameOrAlias);
        if (alias.Length == 0 || !_aliasIndex.TryGetValue(alias, out var keys))
            return new CatalogResolution(CatalogResolutionStatus.Unknown, null, []);

        var matches = _snapshot
            .Where(a => keys.Contains(CatalogKey(a), StringComparer.OrdinalIgnoreCase))
            .Where(a => namespaceName is null ||
                        string.Equals(a.Namespace, namespaceName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.Length switch
        {
            0 => new CatalogResolution(CatalogResolutionStatus.Unknown, null, []),
            1 => new CatalogResolution(CatalogResolutionStatus.Resolved, matches[0], matches),
            _ => new CatalogResolution(CatalogResolutionStatus.Ambiguous, null, matches)
        };
    }

    public bool IsEnabled(string nameOrAlias) =>
        !TryGet(nameOrAlias, out var app) || app.Enabled;

    public void ReplaceSnapshot(IReadOnlyList<DiscoveredApplication> apps, TimeSpan forgetAfter)
    {
        lock (_gate)
        {
            var previous = _snapshot
                .GroupBy(CatalogKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            var merged = new List<DiscoveredApplication>(apps.Count);

            foreach (var incoming in apps)
            {
                if (previous.Remove(CatalogKey(incoming), out var existing))
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

            Install(merged
                .OrderBy(a => a.Namespace, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }
    }

    public bool SetEnabled(string nameOrAlias, bool enabled, string? namespaceName = null)
    {
        lock (_gate)
        {
            var resolution = Resolve(nameOrAlias, namespaceName);
            if (resolution.Status != CatalogResolutionStatus.Resolved)
                return false;

            var targetKey = CatalogKey(resolution.Application!);

            var updated = new List<DiscoveredApplication>(_snapshot.Count);
            var changed = false;
            foreach (var app in _snapshot)
            {
                if (CatalogKey(app).Equals(targetKey, StringComparison.OrdinalIgnoreCase))
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
        var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            var appKey = CatalogKey(app);
            foreach (var alias in new[]
                     {
                         app.Name, app.DeploymentName, app.KubernetesServiceName,
                         app.OtelServiceName, $"{app.Namespace}/{app.Name}"
                     })
            {
                var key = NameNormalizer.Normalize(alias);
                if (key.Length > 0)
                {
                    if (!index.TryGetValue(key, out var matches))
                    {
                        matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        index[key] = matches;
                    }
                    matches.Add(appKey);
                }
            }
        }

        _aliasIndex = index.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.Ordinal);
        _snapshot = apps;
    }

    private static string CatalogKey(DiscoveredApplication app) =>
        $"{app.Namespace ?? "~"}/{app.Name}";
}
