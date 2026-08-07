using System.ComponentModel;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Engines.Correlation;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services.Contracts;
using ModelContextProtocol.Server;

namespace McpApis.McpServer.Tools.VNext;

[McpServerToolType]
public sealed class ServiceGetIncidentTimelineTool
{
    [McpServerTool(Name = "service_get_incident_timeline"),
     Description("Builds an evidence-backed incident timeline from anomalies, deployment history, Kubernetes Events/workload state, traces and redacted log fingerprints. Missing optional sources produce a partial response.")]
    public static Task<ObservationEnvelope<IncidentTimeline>> Execute(
        IServiceIdentityResolver resolver,
        ICorrelationEngine engine,
        IOptions<ObservabilityLimitsOptions> limits,
        [Description("Canonical service name or alias.")] string serviceName,
        [Description("Kubernetes namespace. Required when ambiguous.")] string? namespaceName = null,
        [Description("Timeline window in minutes.")] int? windowMinutes = null,
        CancellationToken cancellationToken = default) =>
        VNextToolSupport.ExecuteAsync(
            "service_get_incident_timeline",
            limits.Value,
            async ct =>
            {
                var failure = VNextToolSupport.ResolveOrError<IncidentTimeline>(
                    resolver, serviceName, namespaceName, out var resolution);
                if (failure is not null)
                    return failure;
                if (!VNextToolSupport.TryCreateWindow(windowMinutes, limits.Value, out var window, out var error))
                    return ObservationEnvelope<IncidentTimeline>.Failure(
                        "invalid_window", error!, resolution.Identity,
                        recovery: $"Use 1..{limits.Value.MaxWindowMinutes} minutes.");
                var result = await engine.BuildTimelineAsync(
                    resolution.Identity!, resolution.Application!.Selector, window, ct);
                return ObservationEnvelope<IncidentTimeline>.Success(
                    result.Data, resolution.Identity, window,
                    result.Sources, result.Evidence, result.Warnings);
            },
            cancellationToken);
}
