using System.ComponentModel;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Dependencies;
using McpApis.McpServer.Infrastructure.Caching;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools.VNext;

[McpServerToolType]
public sealed class ServiceGetDependenciesTool
{
    [McpServerTool(Name = "service_get_dependencies"),
     Description("Returns a namespace-safe normalized dependency graph with inbound/outbound edges, bounded depth, cycles, evidence, critical path and potential blast radius.")]
    public static Task<ObservationEnvelope<DependencyGraph>> Execute(
        IServiceIdentityResolver resolver,
        IDependencyEngine engine,
        IObservabilityCache cache,
        IOptions<ObservabilityLimitsOptions> limits,
        IOptions<ObservabilityCacheOptions> cacheOptions,
        [Description("Canonical service name or alias.")] string serviceName,
        [Description("Kubernetes namespace. Required when ambiguous.")] string? namespaceName = null,
        [Description("Graph depth, capped by server policy.")] int depth = 2,
        [Description("Observation window in minutes.")] int? windowMinutes = null,
        CancellationToken cancellationToken = default) =>
        VNextToolSupport.ExecuteAsync(
            "service_get_dependencies",
            limits.Value,
            async ct =>
            {
                var failure = VNextToolSupport.ResolveOrError<DependencyGraph>(
                    resolver, serviceName, namespaceName, out var resolution);
                if (failure is not null)
                    return failure;
                if (depth <= 0 || depth > limits.Value.MaxGraphDepth)
                    return ObservationEnvelope<DependencyGraph>.Failure(
                        "invalid_depth",
                        $"Depth must be between 1 and {limits.Value.MaxGraphDepth}.",
                        resolution.Identity);
                if (!VNextToolSupport.TryCreateWindow(windowMinutes, limits.Value, out var window, out var error))
                    return ObservationEnvelope<DependencyGraph>.Failure(
                        "invalid_window", error!, resolution.Identity);

                var identity = resolution.Identity!;
                var result = await cache.GetOrCreateAsync(
                    $"dependencies:{identity.Key}:{depth}:{Math.Round(window.Span.TotalMinutes)}",
                    TimeSpan.FromSeconds(cacheOptions.Value.DependencyTtlSeconds),
                    token => engine.AnalyzeAsync(identity, window, depth, token),
                    ct);
                return ObservationEnvelope<DependencyGraph>.Success(
                    result.Data, identity, window, result.Sources, result.Evidence, result.Warnings);
            },
            cancellationToken);
}
