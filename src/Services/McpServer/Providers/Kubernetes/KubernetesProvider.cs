using System.Diagnostics;
using Microsoft.Extensions.Options;
using McpApis.BuildingBlocks.Observability;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Telemetry;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Providers.Kubernetes;

public sealed class KubernetesProvider(
    IKubernetesCollector collector,
    IOptions<SecurityOptions> security,
    IOptions<ObservabilityLimitsOptions> limits,
    ILogger<KubernetesProvider> logger) : IKubernetesProvider
{
    private static readonly IReadOnlySet<string> SensitiveFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "password", "secret", "token", "authorization", "cookie", "apiKey" };

    public async Task<ProviderResult<KubernetesWorkloadState>> GetWorkloadStateAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = ObservabilityTelemetry.ActivitySource.StartActivity("provider.kubernetes.workload");
        activity?.SetTag("provider", "kubernetes");
        if (!IsAllowed(service.Namespace))
            return ProviderResult<KubernetesWorkloadState>.Unavailable(
                "kubernetes", 0, $"Namespace '{service.Namespace}' is outside the allowlist.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.Value.ProviderTimeoutSeconds));
            var raw = await collector.GetWorkloadAsync(
                service.Namespace, service.DeploymentName, selector, timeout.Token);
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (raw is null)
            {
                Record(elapsed, "unavailable");
                return ProviderResult<KubernetesWorkloadState>.Unavailable(
                    "kubernetes", (long)elapsed.TotalMilliseconds,
                    "No Deployment or Pods matched the catalog selector.");
            }

            var pods = raw.Pods.Select(p => new KubernetesPodState(
                p.Name, p.Phase, p.Ready, p.Restarts, p.OomKilled,
                p.CrashLoopBackOff, p.Pending, p.ContainerStates,
                p.ResourceRequests, p.ResourceLimits)).ToArray();
            var value = new KubernetesWorkloadState(
                raw.DeploymentName,
                raw.DesiredReplicas,
                raw.ReadyReplicas,
                raw.AvailableReplicas,
                raw.Revision,
                raw.Image,
                raw.ImageDigest,
                raw.Selector,
                pods,
                pods.Sum(p => p.Restarts),
                pods.Length > 0 && pods.All(p => p.Ready),
                pods.Length > 0);
            Record(elapsed, "available");
            return ProviderResult<KubernetesWorkloadState>.Available(
                "kubernetes", value, DateTimeOffset.UtcNow, (long)elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "timeout");
            return ProviderResult<KubernetesWorkloadState>.Unavailable(
                "kubernetes", (long)elapsed.TotalMilliseconds, "Kubernetes provider timed out.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kubernetes workload collection failed.");
            var elapsed = Stopwatch.GetElapsedTime(started);
            Record(elapsed, "unavailable");
            return ProviderResult<KubernetesWorkloadState>.Unavailable(
                "kubernetes", (long)elapsed.TotalMilliseconds, ex.Message);
        }
    }

    public async Task<ProviderResult<IReadOnlyList<KubernetesEventRecord>>> GetEventsAsync(
        ServiceIdentity service,
        TimeWindow window,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        if (!IsAllowed(service.Namespace))
            return ProviderResult<IReadOnlyList<KubernetesEventRecord>>.Unavailable(
                "events", 0, $"Namespace '{service.Namespace}' is outside the allowlist.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.Value.ProviderTimeoutSeconds));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                service.ServiceName,
                service.DeploymentName ?? string.Empty,
                service.KubernetesServiceName ?? string.Empty
            };
            names.Remove(string.Empty);
            var raw = await collector.ListEventsAsync(service.Namespace, window.From, timeout.Token);
            var filtered = raw.Where(e => names.Any(n =>
                    e.InvolvedName.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                    e.InvolvedName.StartsWith(n + "-", StringComparison.OrdinalIgnoreCase)))
                .Select(e => new KubernetesEventRecord(
                    e.Id, e.Timestamp, e.Type, e.Reason,
                    SensitiveDataRedactor.Redact(e.Message, SensitiveFields),
                    e.InvolvedKind, e.InvolvedName, e.Count))
                .ToArray();
            var elapsed = Stopwatch.GetElapsedTime(started);
            return ProviderResult<IReadOnlyList<KubernetesEventRecord>>.Available(
                "events", filtered, DateTimeOffset.UtcNow, (long)elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Kubernetes event collection failed.");
            var elapsed = Stopwatch.GetElapsedTime(started);
            return ProviderResult<IReadOnlyList<KubernetesEventRecord>>.Unavailable(
                "events", (long)elapsed.TotalMilliseconds,
                ex is OperationCanceledException ? "Kubernetes event query timed out." : ex.Message);
        }
    }

    private bool IsAllowed(string namespaceName) =>
        security.Value.AllowedNamespaces.Contains(namespaceName, StringComparer.OrdinalIgnoreCase);

    private static void Record(TimeSpan elapsed, string availability)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("provider", "kubernetes"),
            new KeyValuePair<string, object?>("availability", availability)
        };
        ObservabilityTelemetry.ProviderCalls.Add(1, tags);
        ObservabilityTelemetry.ProviderDuration.Record(elapsed.TotalMilliseconds, tags);
    }
}
