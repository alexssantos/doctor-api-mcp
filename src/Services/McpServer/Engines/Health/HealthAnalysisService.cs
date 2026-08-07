using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Caching;
using McpApis.McpServer.Infrastructure.Options;

namespace McpApis.McpServer.Engines.Health;

/// <summary>
/// The single cached entry point for health evaluation. Service tools, RCA,
/// the system summary and dashboard all use this exact key and engine result.
/// </summary>
public interface IHealthAnalysisService
{
    Task<AnalysisResult<HealthReport>> EvaluateAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        TimeWindow window,
        CancellationToken cancellationToken = default);
}

public sealed class HealthAnalysisService(
    IHealthEngine engine,
    IObservabilityCache cache,
    IOptions<ObservabilityCacheOptions> cacheOptions) : IHealthAnalysisService
{
    public Task<AnalysisResult<HealthReport>> EvaluateAsync(
        ServiceIdentity service,
        IReadOnlyDictionary<string, string> selector,
        TimeWindow window,
        CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync(
            CacheKey(service, window),
            TimeSpan.FromSeconds(cacheOptions.Value.HealthTtlSeconds),
            token => engine.EvaluateAsync(service, selector, window, token),
            cancellationToken);

    internal static string CacheKey(ServiceIdentity service, TimeWindow window) =>
        $"health:{service.Key}:{Math.Round(window.Span.TotalMinutes)}";
}
