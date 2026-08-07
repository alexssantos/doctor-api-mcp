using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Health;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Engines.SystemHealth;

public interface ISystemHealthEngine
{
    Task<AnalysisResult<SystemHealthSummary>> SummarizeAsync(
        TimeWindow window,
        CancellationToken cancellationToken = default);
}

public sealed class SystemHealthEngine(
    IApplicationCatalog catalog,
    IServiceIdentityResolver resolver,
    IHealthAnalysisService healthAnalysis,
    IOptions<ObservabilityLimitsOptions> limits) : ISystemHealthEngine
{
    public async Task<AnalysisResult<SystemHealthSummary>> SummarizeAsync(
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("engine.system_health");
        activity?.SetTag("engine", "system_health");
        var applications = catalog.GetAll()
            .Where(application => application.Enabled &&
                                  !application.LockedDisabled &&
                                  !string.IsNullOrWhiteSpace(application.Namespace))
            .OrderBy(application => application.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var analyses = new ConcurrentBag<AnalysisResult<HealthReport>>();
        var summaries = new ConcurrentBag<ServiceHealthSummary>();
        var warnings = new ConcurrentBag<string>();
        var concurrency = Math.Clamp(limits.Value.ConcurrencyLimit, 1, 8);

        await Parallel.ForEachAsync(
            applications,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = concurrency
            },
            async (application, ct) =>
            {
                var resolution = resolver.Resolve(application.Name, application.Namespace);
                if (!resolution.IsResolved)
                {
                    warnings.Add(
                        $"Catalog entry {application.Namespace}/{application.Name} could not be resolved for system health.");
                    return;
                }

                try
                {
                    var result = await healthAnalysis.EvaluateAsync(
                        resolution.Identity!, application.Selector, window, ct);
                    analyses.Add(result);
                    summaries.Add(new ServiceHealthSummary(
                        resolution.Identity!,
                        result.Data.HealthStatus,
                        result.Data.Score,
                        result.Data.Coverage,
                        result.Data.Findings.Count(finding =>
                            finding.Severity == FindingSeverity.Critical),
                        result.Data.EvaluatedAt));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add(
                        $"Health analysis for {application.Namespace}/{application.Name} failed deterministically: {ex.GetType().Name}.");
                    summaries.Add(new ServiceHealthSummary(
                        resolution.Identity!, HealthState.Unknown, null, 0, 0,
                        DateTimeOffset.UtcNow));
                }
            });

        var ordered = summaries
            .OrderBy(summary => SortOrder(summary.HealthStatus))
            .ThenBy(summary => summary.Score ?? double.MinValue)
            .ThenBy(summary => summary.Service.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var counts = ordered.GroupBy(summary => summary.HealthStatus)
            .ToDictionary(group => group.Key, group => group.Count());
        var overall = OverallHealth(ordered);
        var report = new SystemHealthSummary(
            overall,
            ordered.Length,
            counts.GetValueOrDefault(HealthState.Healthy),
            counts.GetValueOrDefault(HealthState.Degraded),
            counts.GetValueOrDefault(HealthState.Critical),
            counts.GetValueOrDefault(HealthState.Unknown),
            ordered,
            DateTimeOffset.UtcNow);
        var analysisArray = analyses.ToArray();
        var sources = AggregateSources(analysisArray.SelectMany(item => item.Sources));
        var allWarnings = warnings
            .Concat(analysisArray.SelectMany(item => item.Warnings))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new AnalysisResult<SystemHealthSummary>(report, sources, [], allWarnings);
    }

    private static IReadOnlyList<SourceStatus> AggregateSources(IEnumerable<SourceStatus> sources) =>
        sources.GroupBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToArray();
                var availability = items.All(item =>
                    item.Availability == SourceAvailability.Available)
                    ? SourceAvailability.Available
                    : items.All(item => item.Availability == SourceAvailability.Unavailable)
                        ? SourceAvailability.Unavailable
                        : SourceAvailability.Stale;
                return new SourceStatus(
                    group.Key,
                    availability,
                    items.Select(item => item.ObservedAt).DefaultIfEmpty().Max(),
                    items.Select(item => item.FreshnessSeconds).DefaultIfEmpty().Max(),
                    items.Sum(item => item.ElapsedMilliseconds),
                    items.SelectMany(item => item.Warnings).Distinct().ToArray());
            })
            .OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static HealthState OverallHealth(IReadOnlyList<ServiceHealthSummary> summaries)
    {
        if (summaries.Count == 0 || summaries.All(item => item.HealthStatus == HealthState.Unknown))
            return HealthState.Unknown;
        if (summaries.Any(item => item.HealthStatus == HealthState.Critical))
            return HealthState.Critical;
        if (summaries.Any(item => item.HealthStatus is HealthState.Degraded or HealthState.Unknown))
            return HealthState.Degraded;
        return HealthState.Healthy;
    }

    private static int SortOrder(HealthState state) => state switch
    {
        HealthState.Critical => 0,
        HealthState.Degraded => 1,
        HealthState.Unknown => 2,
        _ => 3
    };
}
