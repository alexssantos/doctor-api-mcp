using System.ComponentModel;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Caching;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Providers.Contracts;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools.VNext;

[McpServerToolType]
public sealed class ServiceGetSpecTool
{
    [McpServerTool(Name = "service_get_spec"),
     Description("Returns a versioned, bounded service specification with namespace-aware identity, workload metadata, summarized endpoints and explicit signal coverage. Raw OpenAPI is intentionally excluded.")]
    public static Task<ObservationEnvelope<ServiceSpecReport>> Execute(
        IServiceIdentityResolver resolver,
        IApplicationSpecProvider provider,
        IObservabilityCache cache,
        IOptions<ObservabilityLimitsOptions> limits,
        IOptions<ObservabilityCacheOptions> cacheOptions,
        [Description("Canonical service name or alias.")] string serviceName,
        [Description("Kubernetes namespace. Required when the service name is ambiguous.")] string? namespaceName = null,
        CancellationToken cancellationToken = default) =>
        VNextToolSupport.ExecuteAsync(
            "service_get_spec",
            limits.Value,
            async ct =>
            {
                var failure = VNextToolSupport.ResolveOrError<ServiceSpecReport>(
                    resolver, serviceName, namespaceName, out var resolution);
                if (failure is not null)
                    return failure;

                var identity = resolution.Identity!;
                var result = await cache.GetOrCreateAsync(
                    $"spec:{identity.Key}",
                    TimeSpan.FromSeconds(cacheOptions.Value.SpecTtlSeconds),
                    token => provider.GetSpecAsync(identity, token),
                    ct);
                if (result.Value is null)
                    return ObservationEnvelope<ServiceSpecReport>.Failure(
                        "source_unavailable",
                        result.Warnings.FirstOrDefault() ?? "Application specification is unavailable.",
                        identity);

                return ObservationEnvelope<ServiceSpecReport>.Success(
                    result.Value,
                    identity,
                    null,
                    [result.ToSourceStatus()],
                    warnings: result.Warnings);
            },
            cancellationToken);
}
