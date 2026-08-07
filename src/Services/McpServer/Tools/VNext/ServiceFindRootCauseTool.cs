using System.ComponentModel;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.RootCause;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools.VNext;

[McpServerToolType]
public sealed class ServiceFindRootCauseTool
{
    [McpServerTool(Name = "service_find_root_cause"),
     Description("Ranks deterministic root-cause hypotheses from the correlated timeline, health and dependency graph. Returns supporting/contradicting evidence, confidence, coverage, limitations and non-executable recommendations; insufficient evidence is inconclusive.")]
    public static Task<ObservationEnvelope<RootCauseReport>> Execute(
        IServiceIdentityResolver resolver,
        IRootCauseEngine engine,
        IOptions<ObservabilityLimitsOptions> limits,
        [Description("Canonical service name or alias.")] string serviceName,
        [Description("Kubernetes namespace. Required when ambiguous.")] string? namespaceName = null,
        [Description("Analysis window in minutes.")] int? windowMinutes = null,
        [Description("Dependency traversal depth.")] int depth = 2,
        CancellationToken cancellationToken = default) =>
        VNextToolSupport.ExecuteAsync(
            "service_find_root_cause",
            limits.Value,
            async ct =>
            {
                var failure = VNextToolSupport.ResolveOrError<RootCauseReport>(
                    resolver, serviceName, namespaceName, out var resolution);
                if (failure is not null)
                    return failure;
                if (!VNextToolSupport.TryCreateWindow(windowMinutes, limits.Value, out var window, out var error))
                    return ObservationEnvelope<RootCauseReport>.Failure(
                        "invalid_window", error!, resolution.Identity,
                        recovery: $"Use 1..{limits.Value.MaxWindowMinutes} minutes.");
                if (depth < 1 || depth > limits.Value.MaxGraphDepth)
                    return ObservationEnvelope<RootCauseReport>.Failure(
                        "invalid_depth",
                        $"Depth must be between 1 and {limits.Value.MaxGraphDepth}.",
                        resolution.Identity,
                        window);
                var result = await engine.AnalyzeAsync(
                    resolution.Identity!, resolution.Application!.Selector,
                    window, depth, ct);
                return ObservationEnvelope<RootCauseReport>.Success(
                    result.Data, resolution.Identity, window,
                    result.Sources, result.Evidence, result.Warnings);
            },
            cancellationToken);
}
