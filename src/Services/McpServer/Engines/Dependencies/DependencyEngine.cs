using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Engines.Dependencies;

public interface IDependencyEngine
{
    Task<AnalysisResult<DependencyGraph>> AnalyzeAsync(
        ServiceIdentity root,
        TimeWindow window,
        int depth,
        CancellationToken cancellationToken = default);
}

public sealed class DependencyEngine(
    ITraceProvider traceProvider,
    IApplicationCatalog catalog,
    IServiceIdentityResolver resolver,
    IOptions<ObservabilityLimitsOptions> limits) : IDependencyEngine
{
    public async Task<AnalysisResult<DependencyGraph>> AnalyzeAsync(
        ServiceIdentity root,
        TimeWindow window,
        int depth,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("engine.dependencies");
        activity?.SetTag("engine", "dependencies");
        var cappedDepth = Math.Clamp(depth, 1, limits.Value.MaxGraphDepth);
        var observed = await traceProvider.GetDependenciesAsync(window, cancellationToken);
        var evidence = new List<Evidence>();
        var candidates = new List<EdgeCandidate>();

        if (observed.Value is not null)
        {
            foreach (var item in observed.Value)
            {
                var source = resolver.Resolve(item.SourceService);
                var target = resolver.Resolve(item.TargetService);
                if (!source.IsResolved || !target.IsResolved)
                    continue;
                candidates.Add(new EdgeCandidate(
                    source.Identity!, target.Identity!, item.ObservedAt,
                    item.CallCount, item.ErrorCount,
                    item.AverageLatencyMilliseconds, Observed: true, Declared: false));
            }
        }

        foreach (var app in catalog.GetAll().Where(a => a.Enabled && a.DeclaredDependencies.Count > 0))
        {
            if (string.IsNullOrWhiteSpace(app.Namespace))
                continue;
            var source = resolver.Resolve(app.Name, app.Namespace);
            if (!source.IsResolved)
                continue;
            foreach (var dependency in app.DeclaredDependencies)
            {
                var target = resolver.Resolve(dependency);
                if (!target.IsResolved)
                    continue;
                candidates.Add(new EdgeCandidate(
                    source.Identity!, target.Identity!, app.LastSeen,
                    0, 0, null, Observed: false, Declared: true));
            }
        }

        var merged = candidates
            .GroupBy(c => $"{c.Source.Key}->{c.Target.Key}", StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge(group, evidence))
            .ToArray();
        var reachableKeys = FindReachable(root.Key, merged, cappedDepth);
        var edges = merged
            .Where(edge => reachableKeys.Contains(edge.Source.Key) && reachableKeys.Contains(edge.Target.Key))
            .Take(limits.Value.MaxDependencies)
            .ToArray();
        var nodes = edges.SelectMany(e => new[] { e.Source, e.Target })
            .Append(root)
            .DistinctBy(service => service.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(service => service.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var inbound = edges.Where(e =>
                e.Target.Key.Equals(root.Key, StringComparison.OrdinalIgnoreCase) ||
                IsConnectedToRoot(e.Target.Key, root.Key, edges, inbound: true, cappedDepth))
            .ToArray();
        var outbound = edges.Where(e =>
                e.Source.Key.Equals(root.Key, StringComparison.OrdinalIgnoreCase) ||
                IsConnectedToRoot(e.Source.Key, root.Key, edges, inbound: false, cappedDepth))
            .ToArray();
        var cycles = FindCycles(edges, cappedDepth);
        var criticalPath = FindCriticalPath(root.Key, edges, cappedDepth);
        var blastRadius = Traverse(root.Key, edges, cappedDepth, inbound: true)
            .Where(key => !key.Equals(root.Key, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var graph = new DependencyGraph(
            root, cappedDepth, nodes, inbound, outbound, cycles, criticalPath, blastRadius);
        var catalogSource = new SourceStatus(
            "catalog", SourceAvailability.Available, DateTimeOffset.UtcNow, 0, 0, []);
        return new AnalysisResult<DependencyGraph>(
            graph,
            [observed.ToSourceStatus(), catalogSource],
            evidence,
            observed.Warnings);
    }

    private static DependencyEdge Merge(IEnumerable<EdgeCandidate> group, List<Evidence> evidence)
    {
        var entries = group.ToArray();
        var source = entries[0].Source;
        var target = entries[0].Target;
        var observed = entries.Where(e => e.Observed).ToArray();
        var callCount = observed.Sum(e => e.CallCount);
        var errorCount = observed.Sum(e => e.ErrorCount);
        var latency = observed.Where(e => e.LatencyMilliseconds is not null)
            .Select(e => e.LatencyMilliseconds!.Value)
            .DefaultIfEmpty()
            .Average();
        var ids = new List<string>();
        if (observed.Length > 0)
        {
            var id = $"trace:dependency:{evidence.Count + 1}";
            evidence.Add(new Evidence(
                id, "traces", "dependency_calls", callCount, null, "count",
                observed.Max(e => e.ObservedAt), "jaeger_dependency_graph",
                $"{source.Key}->{target.Key}"));
            ids.Add(id);
        }
        if (entries.Any(e => e.Declared))
        {
            var id = $"spec:dependency:{evidence.Count + 1}";
            evidence.Add(new Evidence(
                id, "application_spec", "declared_dependency", 1, null, "boolean",
                entries.Max(e => e.ObservedAt), "catalog_annotation:dependencies",
                $"{source.Key}->{target.Key}"));
            ids.Add(id);
        }
        return new DependencyEdge(
            source,
            target,
            "service_call",
            entries.Max(e => e.ObservedAt),
            callCount,
            callCount > 0 ? (double)errorCount / callCount : null,
            observed.Any(e => e.LatencyMilliseconds is not null) ? latency : null,
            ids,
            entries.Any(e => e.Declared),
            observed.Length > 0);
    }

    private static HashSet<string> FindReachable(
        string root,
        IReadOnlyList<DependencyEdge> edges,
        int depth)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = edges
                .Where(e => frontier.Contains(e.Source.Key) || frontier.Contains(e.Target.Key))
                .SelectMany(e => new[] { e.Source.Key, e.Target.Key })
                .Where(result.Add)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            frontier = next;
        }
        return result;
    }

    private static bool IsConnectedToRoot(
        string candidate,
        string root,
        IReadOnlyList<DependencyEdge> edges,
        bool inbound,
        int depth) =>
        Traverse(root, edges, depth, inbound).Contains(candidate);

    private static HashSet<string> Traverse(
        string root,
        IReadOnlyList<DependencyEdge> edges,
        int depth,
        bool inbound)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = edges
                .Where(e => inbound ? frontier.Contains(e.Target.Key) : frontier.Contains(e.Source.Key))
                .Select(e => inbound ? e.Source.Key : e.Target.Key)
                .Where(visited.Add)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            frontier = next;
        }
        return visited;
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindCycles(
        IReadOnlyList<DependencyEdge> edges,
        int maxDepth)
    {
        var cycles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in edges.Select(e => e.Source.Key).Distinct(StringComparer.OrdinalIgnoreCase))
            Search(start, start, [start], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start });
        return cycles.Values.ToArray();

        void Search(string start, string current, List<string> path, HashSet<string> visited)
        {
            if (path.Count > maxDepth + 1)
                return;
            foreach (var target in edges.Where(e => e.Source.Key.Equals(current, StringComparison.OrdinalIgnoreCase))
                         .Select(e => e.Target.Key))
            {
                if (target.Equals(start, StringComparison.OrdinalIgnoreCase) && path.Count > 1)
                {
                    var cycle = path.Append(start).ToArray();
                    var canonical = string.Join("|", cycle.Take(cycle.Length - 1)
                        .Order(StringComparer.OrdinalIgnoreCase));
                    cycles.TryAdd(canonical, cycle);
                }
                else if (visited.Add(target))
                {
                    path.Add(target);
                    Search(start, target, path, visited);
                    path.RemoveAt(path.Count - 1);
                    visited.Remove(target);
                }
            }
        }
    }

    private static IReadOnlyList<string> FindCriticalPath(
        string root,
        IReadOnlyList<DependencyEdge> edges,
        int depth)
    {
        var path = new List<string> { root };
        var current = root;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        for (var level = 0; level < depth; level++)
        {
            var next = edges
                .Where(e => e.Source.Key.Equals(current, StringComparison.OrdinalIgnoreCase) &&
                            !visited.Contains(e.Target.Key))
                .OrderByDescending(e => Math.Max(1, e.CallCount) * Math.Max(1, e.LatencyMilliseconds ?? 1))
                .FirstOrDefault();
            if (next is null)
                break;
            current = next.Target.Key;
            visited.Add(current);
            path.Add(current);
        }
        return path;
    }

    private sealed record EdgeCandidate(
        ServiceIdentity Source,
        ServiceIdentity Target,
        DateTimeOffset ObservedAt,
        long CallCount,
        long ErrorCount,
        double? LatencyMilliseconds,
        bool Observed,
        bool Declared);
}
