using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;

namespace McpApis.McpServer.Engines.Health;

public interface IHealthEngine
{
    Task<AnalysisResult<HealthReport>> EvaluateAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        TimeWindow window,
        CancellationToken cancellationToken = default);
}

public sealed class HealthEngine(
    IMetricsProvider metricsProvider,
    IKubernetesProvider kubernetesProvider,
    IOptions<HealthEngineOptions> options) : IHealthEngine
{
    public async Task<AnalysisResult<HealthReport>> EvaluateAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("engine.health");
        activity?.SetTag("engine", "health");
        var metricsTask = metricsProvider.GetRedMetricsAsync(service, window, cancellationToken);
        var kubernetesTask = kubernetesProvider.GetWorkloadStateAsync(service, selector, cancellationToken);
        await Task.WhenAll(metricsTask, kubernetesTask);
        var metrics = await metricsTask;
        var kubernetes = await kubernetesTask;

        var evidence = new List<Evidence>();
        var findings = new List<Finding>();
        var dimensions = new List<HealthDimension>();
        var settings = options.Value;

        AddAvailability(service, metrics, kubernetes, settings, evidence, findings, dimensions);
        AddErrors(service, metrics, settings, evidence, findings, dimensions);
        AddLatency(service, metrics, settings, evidence, findings, dimensions);
        AddSaturation(service, metrics, settings, evidence, findings, dimensions);
        AddStability(service, kubernetes, settings, evidence, findings, dimensions);

        var totalWeight = dimensions.Sum(d => d.Weight);
        var available = dimensions.Where(d => d.Score is not null).ToArray();
        var coveredWeight = available.Sum(d => d.Weight);
        var coverage = totalWeight <= 0 ? 0 : coveredWeight / totalWeight;
        var score = coveredWeight <= 0
            ? (double?)null
            : available.Sum(d => d.Score!.Value * d.Weight) / coveredWeight;
        var missingRequired = dimensions.Any(d => d.Required && d.Score is null);
        var zeroPods = kubernetes.Value is { HasPods: false };

        var health = missingRequired || coverage < settings.MinimumCoverage || score is null
            ? HealthState.Unknown
            : score >= settings.HealthyScore && coverage >= settings.HealthyCoverage && !zeroPods
                ? HealthState.Healthy
                : score >= settings.DegradedScore
                    ? HealthState.Degraded
                    : HealthState.Critical;

        if (zeroPods && kubernetes.Availability != SourceAvailability.Unavailable)
            health = HealthState.Critical;

        foreach (var finding in findings)
        {
            ObservabilityTelemetry.Findings.Add(1,
                new KeyValuePair<string, object?>("finding.type", finding.Type),
                new KeyValuePair<string, object?>("finding.severity", finding.Severity.ToString().ToLowerInvariant()));
        }

        var report = new HealthReport(
            health,
            score is null ? null : Math.Round(score.Value, 2),
            Math.Round(coverage, 4),
            dimensions,
            findings.OrderByDescending(f => f.Severity).ToArray(),
            DateTimeOffset.UtcNow);
        var warnings = metrics.Warnings.Concat(kubernetes.Warnings).Distinct().ToArray();
        return new AnalysisResult<HealthReport>(
            report,
            [metrics.ToSourceStatus(), kubernetes.ToSourceStatus()],
            evidence,
            warnings);
    }

    private static void AddAvailability(
        ServiceIdentity service,
        ProviderResult<RedMetrics> metrics,
        ProviderResult<KubernetesWorkloadState> kubernetes,
        HealthEngineOptions settings,
        List<Evidence> evidence,
        List<Finding> findings,
        List<HealthDimension> dimensions)
    {
        double? score = null;
        var ids = new List<string>();
        if (kubernetes.Value is { } workload)
        {
            var readiness = workload.DesiredReplicas > 0
                ? Math.Clamp((double)workload.ReadyReplicas / workload.DesiredReplicas, 0, 1)
                : 0;
            var metricAvailability = metrics.Value?.Availability?.Value;
            score = metricAvailability is null
                ? readiness * 100
                : (readiness * 0.7 + Math.Clamp(metricAvailability.Value, 0, 1) * 0.3) * 100;
            ids.Add(AddEvidence(evidence, service, "kubernetes", "ready_replica_ratio", readiness,
                null, "ratio", "deployment_status"));
            if (metricAvailability is not null)
                ids.Add(AddEvidence(evidence, service, "metrics", "availability", metricAvailability,
                    1, "ratio", "prometheus_template:availability"));
            if (!workload.HasPods || readiness == 0)
                findings.Add(Finding(service, "availability", FindingSeverity.Critical,
                    "No ready Pods were found for the workload.", ids));
            else if (readiness < 1)
                findings.Add(Finding(service, "availability", FindingSeverity.Warning,
                    $"Only {workload.ReadyReplicas}/{workload.DesiredReplicas} replicas are ready.", ids));
        }
        AddDimension(dimensions, settings, "availability", score, ids);
    }

    private static void AddErrors(
        ServiceIdentity service,
        ProviderResult<RedMetrics> metrics,
        HealthEngineOptions settings,
        List<Evidence> evidence,
        List<Finding> findings,
        List<HealthDimension> dimensions)
    {
        var value = metrics.Value?.ErrorRate?.Value;
        var ids = new List<string>();
        double? score = null;
        if (value is not null)
        {
            score = ScoreLowerIsBetter(value.Value, settings.WarningErrorRate, settings.CriticalErrorRate);
            ids.Add(AddEvidence(evidence, service, "metrics", "error_rate", value,
                settings.WarningErrorRate, "ratio", "prometheus_template:error_rate"));
            if (value >= settings.CriticalErrorRate)
                findings.Add(Finding(service, "error_rate", FindingSeverity.Critical,
                    $"Error rate is {value:P2}, above the critical threshold.", ids));
            else if (value >= settings.WarningErrorRate)
                findings.Add(Finding(service, "error_rate", FindingSeverity.Warning,
                    $"Error rate is {value:P2}, above the warning threshold.", ids));
        }
        AddDimension(dimensions, settings, "errors", score, ids);
    }

    private static void AddLatency(
        ServiceIdentity service,
        ProviderResult<RedMetrics> metrics,
        HealthEngineOptions settings,
        List<Evidence> evidence,
        List<Finding> findings,
        List<HealthDimension> dimensions)
    {
        var value = metrics.Value?.P95Latency?.Value;
        var ids = new List<string>();
        double? score = null;
        if (value is not null)
        {
            score = ScoreLowerIsBetter(value.Value,
                settings.WarningP95Milliseconds, settings.CriticalP95Milliseconds);
            ids.Add(AddEvidence(evidence, service, "metrics", "latency_p95", value,
                settings.WarningP95Milliseconds, "ms", "prometheus_template:latency_p95"));
            if (value >= settings.CriticalP95Milliseconds)
                findings.Add(Finding(service, "latency_p95", FindingSeverity.Critical,
                    $"P95 latency is {value:0.##} ms, above the critical threshold.", ids));
            else if (value >= settings.WarningP95Milliseconds)
                findings.Add(Finding(service, "latency_p95", FindingSeverity.Warning,
                    $"P95 latency is {value:0.##} ms, above the warning threshold.", ids));
        }
        AddDimension(dimensions, settings, "latency", score, ids);
    }

    private static void AddSaturation(
        ServiceIdentity service,
        ProviderResult<RedMetrics> metrics,
        HealthEngineOptions settings,
        List<Evidence> evidence,
        List<Finding> findings,
        List<HealthDimension> dimensions)
    {
        var cpu = metrics.Value?.CpuUsage?.Value;
        var memory = metrics.Value?.MemoryUsage?.Value;
        var ids = new List<string>();
        double? score = null;
        if (cpu is not null || memory is not null)
        {
            var cpuScore = cpu is null ? 100 : ScoreLowerIsBetter(cpu.Value, 0.8, 1.5);
            var memoryScore = memory is null ? 100 : ScoreLowerIsBetter(memory.Value, 512 * 1024 * 1024, 1024 * 1024 * 1024);
            score = Math.Min(cpuScore, memoryScore);
            if (cpu is not null)
                ids.Add(AddEvidence(evidence, service, "metrics", "cpu_usage", cpu, 0.8,
                    "cores", "prometheus_template:cpu_usage"));
            if (memory is not null)
                ids.Add(AddEvidence(evidence, service, "metrics", "memory_usage", memory,
                    512 * 1024 * 1024, "bytes", "prometheus_template:memory_usage"));
            if (score < 60)
                findings.Add(Finding(service, "saturation", FindingSeverity.Warning,
                    "Process CPU or memory usage is above the initial saturation threshold.", ids));
        }
        AddDimension(dimensions, settings, "saturation", score, ids);
    }

    private static void AddStability(
        ServiceIdentity service,
        ProviderResult<KubernetesWorkloadState> kubernetes,
        HealthEngineOptions settings,
        List<Evidence> evidence,
        List<Finding> findings,
        List<HealthDimension> dimensions)
    {
        var ids = new List<string>();
        double? score = null;
        if (kubernetes.Value is { } workload)
        {
            score = ScoreLowerIsBetter(workload.RestartCount,
                settings.WarningRestarts, settings.CriticalRestarts);
            if (workload.Pods.Any(p => p.OomKilled || p.CrashLoopBackOff))
                score = 0;
            ids.Add(AddEvidence(evidence, service, "kubernetes", "pod_restarts", workload.RestartCount,
                settings.WarningRestarts, "count", "pod_container_status"));
            if (workload.Pods.Any(p => p.OomKilled))
                findings.Add(Finding(service, "oom_killed", FindingSeverity.Critical,
                    "At least one container was terminated by OOMKilled.", ids));
            if (workload.Pods.Any(p => p.CrashLoopBackOff))
                findings.Add(Finding(service, "crash_loop", FindingSeverity.Critical,
                    "At least one container is in CrashLoopBackOff.", ids));
            else if (workload.RestartCount >= settings.WarningRestarts)
                findings.Add(Finding(service, "pod_restarts",
                    workload.RestartCount >= settings.CriticalRestarts
                        ? FindingSeverity.Critical : FindingSeverity.Warning,
                    $"Workload containers report {workload.RestartCount} restart(s).", ids));
        }
        AddDimension(dimensions, settings, "stability", score, ids);
    }

    private static void AddDimension(
        List<HealthDimension> dimensions,
        HealthEngineOptions settings,
        string name,
        double? score,
        IReadOnlyList<string> evidenceIds)
    {
        var required = settings.RequiredDimensions.Contains(name, StringComparer.OrdinalIgnoreCase);
        dimensions.Add(new HealthDimension(
            name,
            settings.Weights.GetValueOrDefault(name, 0),
            score is null ? null : Math.Round(Math.Clamp(score.Value, 0, 100), 2),
            required,
            score is null ? SourceAvailability.Unavailable : SourceAvailability.Available,
            evidenceIds));
    }

    private static double ScoreLowerIsBetter(double value, double warning, double critical)
    {
        if (value <= warning) return 100;
        if (value >= critical) return 0;
        return 100 * (critical - value) / (critical - warning);
    }

    private static string AddEvidence(
        List<Evidence> evidence,
        ServiceIdentity service,
        string source,
        string signal,
        double? value,
        double? baseline,
        string unit,
        string descriptor)
    {
        var id = $"{source}:{signal}:{evidence.Count + 1}";
        evidence.Add(new Evidence(
            id, source, signal, value, baseline, unit,
            DateTimeOffset.UtcNow, descriptor, service.Key));
        return id;
    }

    private static Finding Finding(
        ServiceIdentity service,
        string type,
        FindingSeverity severity,
        string message,
        IReadOnlyList<string> evidenceIds) =>
        new(type, severity, service, message, DateTimeOffset.UtcNow, evidenceIds.ToArray());
}
