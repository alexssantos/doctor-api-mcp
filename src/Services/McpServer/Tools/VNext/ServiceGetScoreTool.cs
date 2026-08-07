using System.ComponentModel;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Health;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools.VNext;

[McpServerToolType]
public sealed class ServiceGetScoreTool
{
    [McpServerTool(Name = "service_get_score"),
     Description("Projects score, health status and coverage from the cached Health Engine report; it never recalculates independent scoring rules.")]
    public static Task<ObservationEnvelope<HealthScoreProjection>> Execute(
        IServiceIdentityResolver resolver,
        IHealthAnalysisService health,
        IOptions<ObservabilityLimitsOptions> limits,
        [Description("Canonical service name or alias.")] string serviceName,
        [Description("Kubernetes namespace. Required when the name is ambiguous.")] string? namespaceName = null,
        [Description("Analysis window in minutes.")] int? windowMinutes = null,
        CancellationToken cancellationToken = default) =>
        VNextToolSupport.ExecuteAsync(
            "service_get_score",
            limits.Value,
            async ct =>
            {
                var failure = VNextToolSupport.ResolveOrError<HealthScoreProjection>(
                    resolver, serviceName, namespaceName, out var resolution);
                if (failure is not null)
                    return failure;
                if (!VNextToolSupport.TryCreateWindow(windowMinutes, limits.Value, out var window, out var error))
                    return ObservationEnvelope<HealthScoreProjection>.Failure(
                        "invalid_window", error!, resolution.Identity,
                        recovery: $"Use 1..{limits.Value.MaxWindowMinutes} minutes.");

                var identity = resolution.Identity!;
                var result = await health.EvaluateAsync(
                    identity, resolution.Application!.Selector, window, ct);
                var report = result.Data;
                var projection = new HealthScoreProjection(
                    report.HealthStatus, report.Score, report.Coverage, report.EvaluatedAt);
                return ObservationEnvelope<HealthScoreProjection>.Success(
                    projection, identity, window, result.Sources, result.Evidence, result.Warnings);
            },
            cancellationToken);
}
