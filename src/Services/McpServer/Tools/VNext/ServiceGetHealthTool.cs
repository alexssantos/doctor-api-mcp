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
public sealed class ServiceGetHealthTool
{
    [McpServerTool(Name = "service_get_health"),
     Description("Calculates deterministic service health from RED metrics and Kubernetes stability. Returns score, coverage, findings, evidence, freshness and explicit partial/unavailable states.")]
    public static Task<ObservationEnvelope<HealthReport>> Execute(
        IServiceIdentityResolver resolver,
        IHealthAnalysisService health,
        IOptions<ObservabilityLimitsOptions> limits,
        [Description("Canonical service name or alias.")] string serviceName,
        [Description("Kubernetes namespace. Required when the name is ambiguous.")] string? namespaceName = null,
        [Description("Analysis window in minutes. Defaults to 30 and is capped by server policy.")] int? windowMinutes = null,
        CancellationToken cancellationToken = default) =>
        VNextToolSupport.ExecuteAsync(
            "service_get_health",
            limits.Value,
            async ct =>
            {
                var failure = VNextToolSupport.ResolveOrError<HealthReport>(
                    resolver, serviceName, namespaceName, out var resolution);
                if (failure is not null)
                    return failure;
                if (!VNextToolSupport.TryCreateWindow(windowMinutes, limits.Value, out var window, out var error))
                    return ObservationEnvelope<HealthReport>.Failure(
                        "invalid_window", error!, resolution.Identity,
                        recovery: $"Use 1..{limits.Value.MaxWindowMinutes} minutes.");

                var identity = resolution.Identity!;
                var app = resolution.Application!;
                var result = await health.EvaluateAsync(
                    identity, app.Selector, window, ct);
                return ObservationEnvelope<HealthReport>.Success(
                    result.Data, identity, window, result.Sources, result.Evidence, result.Warnings);
            },
            cancellationToken);
}
