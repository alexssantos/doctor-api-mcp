using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;

namespace McpApis.McpServer.Providers.Contracts;

public interface IMetricsProvider
{
    Task<ProviderResult<RedMetrics>> GetRedMetricsAsync(
        ServiceIdentity service,
        TimeWindow window,
        CancellationToken cancellationToken = default);

    Task<ProviderResult<MetricSeries>> GetSeriesAsync(
        ServiceIdentity service,
        MetricSignal signal,
        TimeWindow window,
        CancellationToken cancellationToken = default);
}

public interface ITraceProvider
{
    Task<ProviderResult<IReadOnlyList<NormalizedSpan>>> GetSpansAsync(
        ServiceIdentity service,
        TimeWindow window,
        int maxTraces,
        CancellationToken cancellationToken = default);

    Task<ProviderResult<IReadOnlyList<DependencyObservation>>> GetDependenciesAsync(
        TimeWindow window,
        CancellationToken cancellationToken = default);
}

public interface ILogsProvider
{
    Task<ProviderResult<IReadOnlyList<LogPattern>>> GetErrorPatternsAsync(
        ServiceIdentity service,
        TimeWindow window,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ProviderResult<IReadOnlyList<LogPattern>>> FindByTraceIdAsync(
        ServiceIdentity service,
        string traceId,
        TimeWindow window,
        int limit,
        CancellationToken cancellationToken = default);
}

public interface IKubernetesProvider
{
    Task<ProviderResult<KubernetesWorkloadState>> GetWorkloadStateAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        CancellationToken cancellationToken = default);

    Task<ProviderResult<IReadOnlyList<KubernetesEventRecord>>> GetEventsAsync(
        ServiceIdentity service,
        TimeWindow window,
        CancellationToken cancellationToken = default);
}

public interface IApplicationSpecProvider
{
    Task<ProviderResult<ServiceSpecReport>> GetSpecAsync(
        ServiceIdentity service,
        CancellationToken cancellationToken = default);
}

public interface IDeploymentEventProvider
{
    Task<ProviderResult<IReadOnlyList<DeploymentChange>>> GetChangesAsync(
        ServiceIdentity service,
        TimeWindow window,
        CancellationToken cancellationToken = default);
}
